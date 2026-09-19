using Capsule.Audio;
using Capsule.Diagnostics;

namespace Capsule.Runtime.Audio;

// One streamed voice: a source read ahead of the device on the streaming worker, and a queue the
// game thread hands to the backend. Every source takes this path, an Ogg file and a resident clip's
// samples alike, because a loop region's boundary can fall inside a buffer.
//
// Everything a play needs belongs to the voice and outlives the play: the device queue, the decode
// scratch, the buffers handed to the device, the loop reader, and the cursor over a resident clip's
// samples. A voice that has ended goes back to the streamer's pool and is armed over the next play's
// source in place, so a warm replay allocates nothing. Its format is fixed by the rate and channel
// count it was built for, since the buffer sizes and the queue are. A file-backed source is per play,
// because it holds a file handle.
//
// The two threads meet at the ready and free queues. The source belongs to the worker for the length
// of a play, and the game thread closes it by asking the worker to. Disposing the voice therefore
// does not free the file handle at once, and the voice reaches the pool once the worker has let go
// of it.
internal sealed class StreamedVoice : IAudioVoice
{
    // Enough queued for the device to survive a frame the worker loses to a long decode, and short
    // enough that a pause or a stop is heard immediately.
    private const double BufferSeconds = 0.25;
    private const int ReadyBuffers = 3;

    // The backend asks for more below three queued, so the game thread keeps three.
    private const int QueuedBuffers = 3;

    private readonly Lock _gate = new();
    private readonly Queue<Ready> _ready = new();
    private readonly Queue<byte[]> _free = new();

    private readonly AudioStreamer _streamer;
    private readonly IPcmQueue _queue;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _samplesPerBuffer;
    private readonly float[] _scratch;
    private readonly LoopedPcmReader _reader = new();

    // Owned by the worker for the length of a play.
    private IPcmSource? _source;

    // This voice's cursor over a resident clip's samples, built on the first play of one and pointed
    // at another clip's samples on later plays. Null for a voice that has only streamed files.
    private MemoryPcmSource? _cursor;

    // Set for a voice streaming a resident clip's samples, counted against that clip's residency. A
    // voice streaming its own file retains nothing. Non-null while this voice is out on a play, which
    // makes retiring idempotent.
    private Action? _ended;

    private string _clipName = "";
    private bool _endOfStream;
    private volatile bool _faulted;
    private volatile bool _returning;
    private bool _paused;
    private bool _disposed;

    // Silent and holding no source. The streamer's pool hands one out, and Start makes it sound.
    internal StreamedVoice(AudioStreamer streamer, IPcmQueue queue, int sampleRate, int channels)
    {
        _streamer = streamer;
        _queue = queue;
        _sampleRate = sampleRate;
        _channels = channels;
        _samplesPerBuffer = Math.Max(channels, (int)(sampleRate * BufferSeconds) * channels);
        _scratch = new float[_samplesPerBuffer];
    }

    public bool Finished
    {
        get
        {
            if (_faulted)
            {
                return true;
            }

            lock (_gate)
            {
                return _endOfStream && _ready.Count == 0 && _queue.Pending == 0;
            }
        }
    }

    // What a pooled voice can be played again for. The queue and the buffers are sized for this
    // format.
    internal (int SampleRate, int Channels) Format => (_sampleRate, _channels);

    // Whether the game thread has ended this voice and the worker may release its source.
    internal bool Returning => _returning;

    public void SetGain(float gain) => _queue.SetGain(gain);

    public void SetPitch(float pitch) => _queue.SetPitch(pitch);

    public void SetPan(float pan) => _queue.SetPan(pan);

    public void Pause()
    {
        _paused = true;
        _queue.Pause();
    }

    public void Resume()
    {
        _paused = false;
        _queue.Resume();
    }

    public void Update()
    {
        if (_faulted || _disposed)
        {
            return;
        }

        Submit();

        // A frame the worker lost starves the device and stops it. Restarting here plays on from the
        // buffers just submitted.
        if (!_paused && _queue.Pending > 0 && _queue.Stopped)
        {
            _queue.Play();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.Stop();

        // Taken and cleared before the end is published. The callback holds the resident sound whose
        // samples this voice streamed, and the worker pools the voice as soon as it sees _returning,
        // so a pooled voice still holding that sound would keep it decoded until shutdown. Clearing
        // it here also makes a second Dispose report nothing.
        Action? ended = _ended;
        _ended = null;

        // The source is the worker's, so it is freed by asking the worker. The queue and the buffers
        // stay, so the next play of this format allocates nothing.
        _returning = true;
        _streamer.Wake();
        ended?.Invoke();
    }

    // Sounds this voice over a resident clip's samples. Its own cursor is pointed at them, so a
    // replay opens nothing.
    internal void Start(
        PcmAudio samples,
        in AudioClip clip,
        float gain,
        float pitch,
        float pan,
        bool loop,
        double startSeconds,
        Action? ended)
    {
        _cursor ??= new MemoryPcmSource(samples);
        _cursor.Arm(samples);

        Start(_cursor, clip, gain, pitch, pan, loop, startSeconds, ended);
    }

    // Sounds this voice over source, fresh or played before. Called on the game thread while nothing
    // else holds the voice, since the streamer hands out a voice the worker has released and Add
    // publishes it to the worker last.
    internal void Start(
        IPcmSource source,
        in AudioClip clip,
        float gain,
        float pitch,
        float pan,
        bool loop,
        double startSeconds,
        Action? ended)
    {
        _source = source;

        // Rounded to the frame the clip time names, like the loop region's bounds.
        _reader.Arm(source, clip.LoopRegion, loop, (long)Math.Round(startSeconds * source.SampleRate));
        _clipName = clip.Name;
        _ended = ended;

        _endOfStream = false;
        _faulted = false;
        _returning = false;
        _paused = false;
        _disposed = false;

        _queue.SetGain(gain);
        _queue.SetPitch(pitch);
        _queue.SetPan(pan);

        // The first buffer is decoded here. A quarter second costs about a millisecond, and starting
        // the device with an empty queue would starve it on frame one.
        Fill();
        Submit();
        _queue.Play();

        _streamer.Add(this);
    }

    // Decodes one buffer ahead on the worker thread. Returns whether it did work, letting an idle
    // worker wait instead of spin.
    internal bool Fill()
    {
        byte[]? buffer;

        lock (_gate)
        {
            if (_endOfStream || _ready.Count >= ReadyBuffers)
            {
                return false;
            }

            buffer = _free.Count > 0 ? _free.Dequeue() : null;
        }

        buffer ??= new byte[_samplesPerBuffer * sizeof(short)];

        int samples;
        try
        {
            samples = _reader.Read(_scratch);
        }
        catch (Exception failure)
        {
            Fault(failure);

            return false;
        }

        if (samples == 0)
        {
            lock (_gate)
            {
                _endOfStream = true;
                _free.Enqueue(buffer);
            }

            return true;
        }

        // Outside the lock, so the game thread's submit never waits on a conversion.
        Encode(_scratch.AsSpan(0, samples), buffer);

        lock (_gate)
        {
            _ready.Enqueue(new Ready(buffer, samples * sizeof(short)));
        }

        return true;
    }

    // Called on the worker once this voice is retiring, and on the streamer's teardown after the
    // worker has been joined. The buffers belong to the voice and survive for its next play. What was
    // decoded and never submitted goes back to the free list, so the next play starts on an empty
    // ready queue.
    //
    // The reader and the cursor belong to the voice too, so they are emptied and not dropped. What
    // they hold of the play, a file handle or the ended clip's samples, must not reach the pool,
    // where a waiting voice would keep that clip decoded until shutdown.
    internal void CloseSource()
    {
        _source?.Dispose();
        _source = null;
        _reader.Clear();

        lock (_gate)
        {
            while (_ready.Count > 0)
            {
                _free.Enqueue(_ready.Dequeue().Buffer);
            }
        }
    }

    // Closes the device queue, once the device it belongs to is going away.
    internal void CloseQueue() => _queue.Dispose();

    private void Fault(Exception failure)
    {
        _faulted = true;
        Log.Warning($"audio: streaming '{_clipName}' failed and it falls silent. {failure.Message}");
    }

    // Rounded, not truncated. A sample read back from a 16-bit source must encode to the value it was
    // authored as.
    private static void Encode(ReadOnlySpan<float> samples, Span<byte> target)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)MathF.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            target[i * 2] = (byte)value;
            target[(i * 2) + 1] = (byte)(value >> 8);
        }
    }

    // Hands the device everything it has room for, returning each buffer to the worker's free list.
    private void Submit()
    {
        while (_queue.Pending < QueuedBuffers)
        {
            Ready ready;

            lock (_gate)
            {
                if (_ready.Count == 0)
                {
                    return;
                }

                ready = _ready.Dequeue();
            }

            _queue.Submit(ready.Buffer, ready.Bytes);

            lock (_gate)
            {
                _free.Enqueue(ready.Buffer);
            }

            _streamer.Wake();
        }
    }

    private readonly record struct Ready(byte[] Buffer, int Bytes);
}

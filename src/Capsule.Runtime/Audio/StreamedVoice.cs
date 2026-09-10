using Capsule.Audio;
using Capsule.Diagnostics;

namespace Capsule.Runtime.Audio;

// One streamed voice: a source read ahead of the device on the streaming worker, and a queue the
// game thread hands to the backend. Whatever the source — an Ogg file, or a resident clip's samples
// — this is the path a loop region is repeated on, because a region boundary falls inside a buffer
// rather than at the end of one.
//
// Everything a play needs belongs to the voice and outlives it: the device queue, the decode
// scratch, the buffers handed to the device, the loop reader, and — for a resident clip — the cursor
// over its samples. A retired voice goes back to the streamer's pool and is armed over the next
// play's source in place, so a clip stopped and played over and over allocates them once and a warm
// replay allocates nothing. Its format does not change with it — the buffer sizes and the queue are
// fixed by the rate and channel count it was built for. Only a file-backed source is per play, since
// it is a file handle.
//
// The two threads meet at the ready and free queues alone. The source belongs to the worker for the
// whole of a play — the game thread never touches it, and releases it by asking the worker to, which
// is why disposing the voice does not free the file handle at once, and why the voice reaches the
// pool only once the worker has let go of it.
internal sealed class StreamedVoice : IAudioVoice
{
    // Enough queued for the device to survive a frame the worker loses to a long decode, and short
    // enough that a pause or a stop is heard immediately.
    private const double BufferSeconds = 0.25;
    private const int ReadyBuffers = 3;

    // The backend asks for more below three queued, so three is what the game thread keeps it at.
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

    // Worker-owned for the whole of a play.
    private IPcmSource? _source;

    // This voice's cursor over a resident clip's samples, built on its first play of one and pointed
    // at another clip's samples on every play after it. Null for a voice that has only streamed files.
    private MemoryPcmSource? _cursor;

    // Set for a voice streaming a resident clip's samples, which is counted against that clip's
    // residency the way a pooled one is; a voice streaming its own file retains nothing. Non-null
    // exactly while this voice is out on a play, which is what makes retiring idempotent.
    private Action? _retired;

    private string _clipName = "";
    private bool _endOfStream;
    private volatile bool _faulted;
    private volatile bool _retiring;
    private bool _paused;
    private bool _disposed;

    // Silent and holding no source: what the streamer's pool hands out and what Start makes sound.
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

    // What a pooled voice can be played again for: the queue and the buffers are sized for this
    // alone.
    internal (int SampleRate, int Channels) Format => (_sampleRate, _channels);

    // Whether the game thread has retired this voice and the worker may release its source.
    internal bool Retiring => _retiring;

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

        // A frame the worker lost leaves the device starved, which stops it; it plays on from the
        // buffers just submitted rather than staying silent for the rest of the clip.
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

        // Taken and cleared before retirement is published: the callback holds the resident sound
        // whose samples this voice streamed, and the worker pools the voice the moment it sees
        // _retiring — a pooled voice still holding it would keep that sound decoded until shutdown.
        // Clearing it here is also what makes a second Dispose report nothing.
        Action? retired = _retired;
        _retired = null;

        // The source is the worker's; asking is the only safe way to free it from here. The queue and
        // the buffers stay, which is what makes the next play of this format allocate nothing.
        _retiring = true;
        _streamer.Wake();
        retired?.Invoke();
    }

    // Sounds this voice over a resident clip's samples: its own cursor is pointed at them, so a
    // replay of a resident clip opens nothing.
    internal void Start(
        PcmAudio samples,
        in AudioClip clip,
        float gain,
        float pitch,
        float pan,
        bool loop,
        double startSeconds,
        Action? retired)
    {
        _cursor ??= new MemoryPcmSource(samples);
        _cursor.Arm(samples);

        Start(_cursor, clip, gain, pitch, pan, loop, startSeconds, retired);
    }

    // Sounds this voice over source, whether it is fresh or has been played before. Called on the
    // game thread while nothing else holds the voice: the streamer hands out a voice the worker has
    // already released, and Add publishes it to the worker last.
    internal void Start(
        IPcmSource source,
        in AudioClip clip,
        float gain,
        float pitch,
        float pan,
        bool loop,
        double startSeconds,
        Action? retired)
    {
        _source = source;

        // Rounded to the frame the clip time names, as the loop region's bounds are.
        _reader.Arm(source, clip.LoopRegion, loop, (long)Math.Round(startSeconds * source.SampleRate));
        _clipName = clip.Name;
        _retired = retired;

        _endOfStream = false;
        _faulted = false;
        _retiring = false;
        _paused = false;
        _disposed = false;

        _queue.SetGain(gain);
        _queue.SetPitch(pitch);
        _queue.SetPan(pan);

        // The first buffer is decoded here rather than waited for: a quarter second costs about a
        // millisecond, and starting the device with an empty queue would starve it on frame one.
        Fill();
        Submit();
        _queue.Play();

        _streamer.Add(this);
    }

    // Decodes one buffer ahead on the worker thread. Answers whether it did work, so an idle worker
    // can wait rather than spin.
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

        // Outside the lock: the game thread's submit must never wait on a conversion.
        Encode(_scratch.AsSpan(0, samples), buffer);

        lock (_gate)
        {
            _ready.Enqueue(new Ready(buffer, samples * sizeof(short)));
        }

        return true;
    }

    // Called on the worker once this voice is retiring, and on the streamer's own teardown after the
    // worker has been joined. The buffers are not freed with the source: they are the voice's, and a
    // voice is played again. What was decoded and never submitted goes back to the free list, so the
    // next play starts on an empty ready queue.
    //
    // The reader and the cursor are the voice's too, so they are emptied rather than dropped: what
    // they hold of the play — the file handle, or the retired clip's samples — must not reach the
    // pool, since a voice waiting there would keep that clip decoded until shutdown.
    internal void ReleaseSource()
    {
        _source?.Dispose();
        _source = null;
        _reader.Release();

        lock (_gate)
        {
            while (_ready.Count > 0)
            {
                _free.Enqueue(_ready.Dequeue().Buffer);
            }
        }
    }

    // Ends the queue itself, once the device it belongs to is going away.
    internal void Release() => _queue.Dispose();

    private void Fault(Exception failure)
    {
        _faulted = true;
        Log.Warning($"audio: streaming '{_clipName}' failed, so it falls silent — {failure.Message}");
    }

    // Rounded rather than truncated: a sample read back from a 16-bit source must encode to the
    // value it was authored as, not one step below it.
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

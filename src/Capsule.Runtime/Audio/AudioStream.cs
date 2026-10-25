using Capsule.Audio;
using Capsule.Diagnostics;

namespace Capsule.Runtime.Audio;

// One streamed voice's decoded frames: a fixed ring of float samples the decode worker fills ahead
// and the mix thread reads. The source and the loop reader over it belong to the worker for the
// length of a play, and the loop region is honoured there. The mixer sees a plain run of frames that
// ends only when the source does.
//
// Everything a play needs belongs to the stream and outlives the play: the ring and the loop reader.
// A stream that has ended goes back to the streamer's pool and is armed over the next play's source in
// place, and a warm replay allocates nothing. A file-backed source is per play, because it holds a
// file handle.
//
// The worker owns the written count and the mixer the read count, and each only reads the other's.
// The end flag is published after the last written count. A mixer that sees the end has every frame.
// The game thread arms a stream before the worker or the mixer can see it, and retires it only once
// the mixer has reported the voice ended.
internal sealed class AudioStream
{
    // Samples, which is 16,384 stereo frames: about a third of a second at 48 kHz. The worker looks
    // again every few milliseconds.
    internal const int Capacity = 1 << 15;

    // What the game thread decodes as it arms a play, which covers the mixer's lead before the
    // worker's first pass.
    private const int ArmedSamples = 1 << 13;

    // The least the worker decodes in one pass, which keeps a nearly full ring from costing a call per
    // frame.
    private const int MinimumFill = 1 << 10;

    private const int Mask = Capacity - 1;

    private readonly float[] _ring = new float[Capacity];
    private readonly LoopedPcmReader _reader = new();
    private readonly AudioStreamer _streamer;

    // Owned by the worker for the length of a play.
    private IPcmSource? _source;

    private string _clipName = "";

    // Samples written, published by the worker.
    private long _written;

    // Samples read, published by the mixer.
    private long _read;

    // Set once the source is spent or has faulted, after the last write.
    private volatile bool _ended;

    private volatile bool _returning;

    // The warning for a decode fault, written by whichever thread faulted and logged by the game
    // thread, because a log sink is written only on the simulation thread.
    private string? _fault;

    internal int Channels { get; private set; } = 1;

    internal int SampleRate { get; private set; } = 1;

    // Silent and holding no source. The streamer's pool hands one out, and Start arms it.
    internal AudioStream(AudioStreamer streamer) => _streamer = streamer;

    // Whether the game thread has retired this stream and the worker may release its source.
    internal bool Returning => _returning;

    // Whether the source is spent or has faulted. Every frame it yielded is already counted written.
    internal bool Ended => _ended;

    // The ring the mixer reads, at ReadPosition and below WrittenSamples, masked by Capacity.
    internal float[] Ring => _ring;

    // On the mixer. Its own count, read without a barrier.
    internal long ReadPosition => _read;

    // On the mixer. Read after Ended, which makes a seen end cover every sample.
    internal long WrittenSamples => Volatile.Read(ref _written);

    // On the mixer, after it has read the samples below position.
    internal void Consume(long position) => Volatile.Write(ref _read, position);

    // Arms this stream over source, fresh or played before, starting at startFrame. Called on the game
    // thread while nothing else holds the stream. The first samples are decoded here, which keeps the
    // mixer from starting on an empty ring.
    internal void Start(IPcmSource source, in AudioClip clip, bool loop, long startFrame)
    {
        _source = source;
        _reader.Arm(source, clip.LoopRegion, loop, startFrame);
        _clipName = clip.Name;
        Channels = source.Channels;
        SampleRate = source.SampleRate;

        // A fault the worker recorded while this stream retired from its last play is heard, not dropped.
        ReportFault();

        _written = 0;
        _read = 0;
        _ended = false;
        _returning = false;

        Fill(ArmedSamples);
    }

    // On the game thread, once the mixer has reported this stream's voice ended. The worker closes the
    // source and pools the stream.
    internal void Retire()
    {
        _returning = true;
        _streamer.Wake();
    }

    // Decodes into the ring's free space on the worker. Returns whether it did work, letting an idle
    // worker wait instead of spin.
    internal bool Fill() => Fill(Capacity);

    // Called on the worker once this stream is retiring, and on the streamer's teardown after the
    // worker has been joined. The reader is emptied and not dropped. A stream in the pool holds no
    // file handle.
    internal void CloseSource()
    {
        _source?.Dispose();
        _source = null;
        _reader.Clear();
    }

    // On the game thread. Logs a recorded fault once.
    internal void ReportFault()
    {
        if (Interlocked.Exchange(ref _fault, null) is { } fault)
        {
            Log.Warning(fault);
        }
    }

    private bool Fill(int most)
    {
        if (_ended || _returning)
        {
            return false;
        }

        long written = _written;
        int free = Capacity - (int)(written - Volatile.Read(ref _read));
        if (free < Math.Min(MinimumFill, most))
        {
            return false;
        }

        // One contiguous run, whole frames only. The capacity is even, which keeps a stereo frame from
        // straddling the wrap.
        int start = (int)written & Mask;
        int room = Math.Min(Math.Min(free, most), Capacity - start);
        room -= room % Channels;

        int read;
        try
        {
            read = _reader.Read(_ring.AsSpan(start, room));
        }
        catch (Exception failure)
        {
            Volatile.Write(ref _fault, $"audio: streaming '{_clipName}' failed and it falls silent. {failure.Message}");
            _ended = true;

            return false;
        }

        if (read == 0)
        {
            _ended = true;

            return true;
        }

        Volatile.Write(ref _written, written + read);

        return true;
    }
}

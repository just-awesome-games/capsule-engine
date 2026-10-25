using System.Diagnostics;

namespace Capsule.Runtime.Audio;

// The output the host mixes into while it has no device: frames are discarded as a device at the
// output's rate would play them, by the clock. The mix thread keeps its pace over it, every voice
// keeps time, and the command ring keeps draining until a real output opens again.
internal sealed class SilentOutput : AudioOutput
{
    private readonly long _started = Stopwatch.GetTimestamp();

    // Frames submitted since the start, advanced by the mix thread alone.
    private long _submitted;

    protected internal override int QueuedFrames => (int)Math.Max(0, _submitted - Played);

    // Frames a device would have played since the start.
    private long Played => (long)(Stopwatch.GetElapsedTime(_started).TotalSeconds * SampleRate);

    // A mix that fell behind skips the frames already due, as a device that ran dry plays silence.
    protected internal override void Submit(ReadOnlySpan<float> frames) =>
        _submitted = Math.Max(_submitted, Played) + (frames.Length / Channels);

    public override void Dispose()
    {
    }
}

using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Audio.Vorbis;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class VorbisAllocationTests
{
    private const string FixturePath = "Audio/Fixtures/loop.ogg";
    private const int FrameCount = 176_400;

    // A streamed voice's own scratch: 0.25 s at the fixture's rate and channel count, the size
    // StreamedVoice reads into on its worker.
    private const double BufferSeconds = 0.25;

    // At least twelve seconds measured, past three full loop wraps.
    private const double MeasuredSeconds = 12.0;

    // The vendored decoder must not allocate once warmed up: no per-packet, per-page or
    // per-loop-wrap allocation. This is the contract; no per-packet unit tests of upstream
    // internals belong here.
    //
    // Warm-up covers two full passes of the fixture plus a buffer's margin, not a flat two seconds:
    // loop.ogg's own five Ogg pages are each discovered once, on their first visit, as the first
    // pass reads forward through the file (Ogg's container format offers no whole-file index to
    // consult up front), and the page immediately after the wrap on the first repeat is also a
    // first visit for the reader's small known-page cache (see Ogg/PageReader.cs). A flat two-second
    // warm-up is short of both for this four-second fixture (two seconds is only half a pass) and
    // leaves part of that one-time discovery inside the measured window. Reaching zero allocation
    // there instead would mean scanning the whole file up front on every open, trading streaming's
    // whole point (bounded, incremental I/O) to shave a one-time cost off of only the fixture's own
    // short first loop.
    [Fact]
    public void AVoicePlayingALoopingClip_AllocatesNothingOncePastWarmup()
    {
        string path = Path.Combine(AppContext.BaseDirectory, FixturePath);

        using VorbisReader reader = new(File.OpenRead(path), closeOnDispose: true);
        VorbisPcmSource source = new(reader);

        int samplesPerBuffer = source.Channels * (int)(source.SampleRate * BufferSeconds);
        float[] scratch = new float[samplesPerBuffer];

        LoopedPcmReader loop = new();
        loop.Arm(source, AudioLoopRegion.None, loop: true, startFrame: 0);

        long framesPerSecond = source.SampleRate;
        long warmupFrames = (2L * FrameCount) + samplesPerBuffer / source.Channels;
        long measuredFrames = (long)(framesPerSecond * MeasuredSeconds);

        ReadFrames(loop, scratch, source.Channels, warmupFrames);

        long before = GC.GetAllocatedBytesForCurrentThread();

        int wraps = ReadFrames(loop, scratch, source.Channels, measuredFrames);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(wraps >= 3);
    }

    // Reads at least frameCount frames (rounded up to whatever the last buffer fills), returning
    // how many times the source wrapped back to its start.
    private static int ReadFrames(LoopedPcmReader loop, float[] scratch, int channels, long frameCount)
    {
        long framesRead = 0;
        int wraps = 0;

        while (framesRead < frameCount)
        {
            int written = loop.Read(scratch);
            Assert.True(written > 0);

            long before = framesRead;
            framesRead += written / channels;
            wraps += (int)(framesRead / FrameCount) - (int)(before / FrameCount);
        }

        return wraps;
    }
}

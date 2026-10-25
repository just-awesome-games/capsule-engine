using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Audio.Vorbis;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class VorbisAllocationTests
{
    private const string FixturePath = "Audio/Fixtures/loop.ogg";

    // The fixture's length in frames, which the reader does not expose.
    private const int FrameCount = 176_400;

    // A scratch of 0.25 s at the fixture's rate and channel count. A streamed voice's decode worker
    // reads up to its ring's third of a second in one pass.
    private const double BufferSeconds = 0.25;

    // At least twelve seconds measured, past three full loop wraps.
    private const double MeasuredSeconds = 12.0;

    // The decoder allocates nothing per packet, per page or per loop wrap once warm. The reader
    // discovers each Ogg page on its first visit, and the page after the first wrap is a first visit
    // to its known-page cache. The warm-up is two full passes plus a buffer, which covers both.
    [Fact]
    public void AVoicePlayingALoopingClip_AllocatesNothingOncePastWarmup()
    {
        string path = Path.Combine(AppContext.BaseDirectory, FixturePath);

        using VorbisReader reader = new(File.OpenRead(path));
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

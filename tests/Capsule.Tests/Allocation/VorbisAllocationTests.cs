using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Audio.Vorbis;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class VorbisAllocationTests
{
    // A scratch of 0.25 s at the fixture's rate and channel count. A streamed voice's decode worker
    // reads up to its ring's third of a second in one pass.
    private const double BufferSeconds = 0.25;

    // At least twelve seconds measured, past three full loop wraps.
    private const double MeasuredSeconds = 12.0;

    // The decoder allocates nothing per packet, per page or per loop wrap once warm. The reader
    // discovers each Ogg page on its first visit, and the page after the first wrap is a first visit
    // to its known-page cache. The warm-up is two full passes plus a buffer, which covers both. A clip
    // of five pages stays in the known-page cache, and one of 31 cycles every cache slot through pages
    // of uneven size. The frame count is each fixture's length, which the reader does not expose.
    [Theory]
    [InlineData("loop.ogg", 176_400)]
    [InlineData("many-pages.ogg", 88_200)]
    public void AVoicePlayingALoopingClip_AllocatesNothingOncePastWarmup(string fixture, int frameCount)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Audio/Fixtures", fixture);

        using VorbisReader reader = new(File.OpenRead(path));
        VorbisPcmSource source = new(reader);

        int samplesPerBuffer = source.Channels * (int)(source.SampleRate * BufferSeconds);
        float[] scratch = new float[samplesPerBuffer];

        LoopedPcmReader loop = new();
        loop.Arm(source, AudioLoopRegion.None, loop: true, startFrame: 0);

        long framesPerSecond = source.SampleRate;
        long warmupFrames = (2L * frameCount) + samplesPerBuffer / source.Channels;
        long measuredFrames = (long)(framesPerSecond * MeasuredSeconds);

        ReadFrames(loop, scratch, source.Channels, frameCount, warmupFrames);

        long before = GC.GetAllocatedBytesForCurrentThread();

        int wraps = ReadFrames(loop, scratch, source.Channels, frameCount, measuredFrames);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(wraps >= 3);
    }

    // Reads at least frames frames (rounded up to whatever the last buffer fills), returning how many
    // times the source of frameCount frames wrapped back to its start.
    private static int ReadFrames(LoopedPcmReader loop, float[] scratch, int channels, int frameCount, long frames)
    {
        long framesRead = 0;
        int wraps = 0;

        while (framesRead < frames)
        {
            int written = loop.Read(scratch);
            Assert.True(written > 0);

            long before = framesRead;
            framesRead += written / channels;
            wraps += (int)(framesRead / frameCount) - (int)(before / frameCount);
        }

        return wraps;
    }
}

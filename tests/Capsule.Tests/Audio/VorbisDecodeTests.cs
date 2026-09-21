using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Audio.Vorbis;

namespace Capsule.Tests.Audio;

// The vendored decoder (Capsule.Runtime.Audio.Vorbis) must decode identically to the reference
// NVorbis 0.10.4 package, which Capsule.Tests references directly for exactly this comparison
// (MonoGame carries it too, but only as a runtime dependency, so it does not reach here as a
// compile reference on its own). One canonical contract, two cases: a plain loop and a loop
// region, both driven far enough to exercise a real voice's wrap and re-read paths.
public sealed class VorbisDecodeTests
{
    private const string FixturePath = "Audio/Fixtures/loop.ogg";
    private const int FrameCount = 176_400;
    private const int Channels = 1;
    private const int SampleRate = 44_100;

    // Both sides are the same decoder version (0.10.4) now, so exact bit-for-bit equality holds
    // rather than the float-rounding tolerance a cross-version comparison would need.

    [Fact]
    public void APlainLoop_DecodesIdenticallyToTheReferenceDecoder()
    {
        // First pass is the whole file, then three more full passes past the wrap.
        long frames = FrameCount * 4L;

        float[] vendored = ReadVendored(AudioLoopRegion.None, frames);
        float[] reference = ReadReference(start: 0, end: FrameCount, frames: frames);

        AssertSamplesMatch(vendored, reference);
    }

    [Fact]
    public void ALoopRegion_DecodesIdenticallyToTheReferenceDecoderAcrossTheSeek()
    {
        AudioLoopRegion region = new(1.0, 3.0);
        long start = (long)Math.Round(region.StartSeconds * SampleRate);
        long end = (long)Math.Round(region.EndSeconds * SampleRate);

        // The first pass runs from frame 0 to the region's end (the intro plays once), then three
        // more region-length passes past the seek back to the region's start.
        long frames = end + (end - start) * 3;

        float[] vendored = ReadVendored(region, frames);
        float[] reference = ReadReference(start, end, frames);

        AssertSamplesMatch(vendored, reference);
    }

    private static void AssertSamplesMatch(float[] vendored, float[] reference)
    {
        Assert.Equal(reference.Length, vendored.Length);

        for (int i = 0; i < reference.Length; i++)
        {
            if (vendored[i] != reference[i])
            {
                Assert.Fail($"sample {i} differs: vendored={vendored[i]} reference={reference[i]}");
            }
        }
    }

    // The vendored path a StreamedVoice actually takes: LoopedPcmReader over VorbisPcmSource, 0.25 s
    // buffers.
    private static float[] ReadVendored(AudioLoopRegion region, long frames)
    {
        string path = Path.Combine(AppContext.BaseDirectory, FixturePath);
        using VorbisReader reader = new(File.OpenRead(path), closeOnDispose: true);
        VorbisPcmSource source = new(reader);

        LoopedPcmReader loop = new();
        loop.Arm(source, region, loop: true, startFrame: 0);

        int samplesPerBuffer = Channels * (int)(SampleRate * 0.25);
        float[] scratch = new float[samplesPerBuffer];
        float[] output = new float[frames * Channels];

        long written = 0;
        while (written < output.Length)
        {
            int read = loop.Read(scratch);
            Assert.True(read > 0);

            int toCopy = (int)Math.Min(read, output.Length - written);
            Array.Copy(scratch, 0, output, written, toCopy);
            written += toCopy;
        }

        return output;
    }

    // A plain ReadSamples loop against the reference package, seeking to the loop's own start frame
    // (0 for a plain loop, the region's start for a loop region) whenever it reaches the loop's end.
    // This mirrors LoopedPcmReader's cap-then-rewind algorithm by hand, against the reference
    // decoder directly, rather than reusing LoopedPcmReader for both sides of the comparison.
    private static float[] ReadReference(long start, long end, long frames)
    {
        string path = Path.Combine(AppContext.BaseDirectory, FixturePath);
        using global::NVorbis.VorbisReader reader = new(File.OpenRead(path), closeOnDispose: true);

        float[] scratch = new float[Channels * 4096];
        float[] output = new float[frames * Channels];

        long frame = 0;
        long written = 0;
        bool readSincePass = true;
        while (written < output.Length)
        {
            if (frame >= end)
            {
                Assert.True(readSincePass);
                readSincePass = false;
                frame = start;
                reader.SeekTo(start);
            }

            long roomFrames = end - frame;
            int wanted = (int)Math.Min(scratch.Length, roomFrames * Channels);
            int read = reader.ReadSamples(scratch.AsSpan(0, wanted));
            if (read == 0)
            {
                Assert.True(readSincePass);
                readSincePass = false;
                frame = start;
                reader.SeekTo(start);
                continue;
            }

            readSincePass = true;
            frame += read / Channels;

            int toCopy = (int)Math.Min(read, output.Length - written);
            Array.Copy(scratch, 0, output, written, toCopy);
            written += toCopy;
        }

        return output;
    }
}

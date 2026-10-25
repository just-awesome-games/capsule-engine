using Capsule.Audio;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Audio.Vorbis;

namespace Capsule.Tests.Audio;

// The vendored decoder (Capsule.Runtime.Audio.Vorbis) decodes identically to the NVorbis 0.10.4
// package, which Capsule.Tests references for this comparison. A plain loop and a loop region are driven
// far enough to exercise a real voice's wrap and re-read paths, and a short clip is decoded whole.
// Every fixture is 44.1 kHz.
public sealed class VorbisDecodeTests
{
    private const string Loop = "loop.ogg";
    private const int FrameCount = 176_400;
    private const int SampleRate = 44_100;

    // Both sides are decoder version 0.10.4 and match bit for bit.

    // many-pages.ogg has more pages than the reader's page caches hold, and its revisits evict.
    [Theory]
    [InlineData(Loop, FrameCount)]
    [InlineData("many-pages.ogg", 88_200)]
    public void APlainLoop_DecodesIdenticallyToTheReferenceDecoder(string fixture, int frameCount)
    {
        // First pass is the whole file, then three more full passes past the wrap.
        long frames = frameCount * 4L;

        float[] vendored = ReadVendored(fixture, AudioLoopRegion.None, frames);
        float[] reference = ReadReference(fixture, start: 0, end: frameCount, frames: frames);

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

        float[] vendored = ReadVendored(Loop, region, frames);
        float[] reference = ReadReference(Loop, start, end, frames);

        AssertSamplesMatch(vendored, reference);
    }

    // A short clip held resident is the reference decode at 16 bits. Decoding stops at the end of the
    // file or one frame past the build's measure, whichever comes first.
    [Theory]
    [InlineData(FrameCount, FrameCount)]
    [InlineData(SampleRate, SampleRate + 1)]
    public void AResidentClip_HoldsTheReferenceDecodeAt16BitsUpToItsMeasure(int measuredFrames, int heldFrames)
    {
        PcmAudio resident = AudioFiles.ReadResident(new FixturePlatform(), new AudioClip("loop", ".ogg", measuredFrames / (double)SampleRate));
        float[] reference = ReadReference(Loop, start: 0, end: FrameCount, frames: heldFrames);

        Assert.Equal(heldFrames, resident.Frames);
        Assert.Equal(SampleRate, resident.SampleRate);
        for (int i = 0; i < reference.Length; i++)
        {
            Assert.Equal(reference[i], resident.Pcm16![i] * PcmAudio.Pcm16Scale, 1f / 32768f);
        }
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

    // The vendored path a stream's worker takes: LoopedPcmReader over VorbisPcmSource, here in 0.25 s
    // buffers.
    private static float[] ReadVendored(string fixture, AudioLoopRegion region, long frames)
    {
        using VorbisReader reader = new(File.OpenRead(FixturePath(fixture)));
        VorbisPcmSource source = new(reader);

        LoopedPcmReader loop = new();
        loop.Arm(source, region, loop: true, startFrame: 0);

        int samplesPerBuffer = source.Channels * (int)(SampleRate * 0.25);
        float[] scratch = new float[samplesPerBuffer];
        float[] output = new float[frames * source.Channels];

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
    private static float[] ReadReference(string fixture, long start, long end, long frames)
    {
        using global::NVorbis.VorbisReader reader = new(File.OpenRead(FixturePath(fixture)), closeOnDispose: true);
        int channels = reader.Channels;

        float[] scratch = new float[channels * 4096];
        float[] output = new float[frames * channels];

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
            int wanted = (int)Math.Min(scratch.Length, roomFrames * channels);
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
            frame += read / channels;

            int toCopy = (int)Math.Min(read, output.Length - written);
            Array.Copy(scratch, 0, output, written, toCopy);
            written += toCopy;
        }

        return output;
    }

    private static string FixturePath(string fixture) => Path.Combine(AppContext.BaseDirectory, "Audio/Fixtures", fixture);

    private sealed class FixturePlatform : HostPlatform
    {
        protected internal override Stream OpenContent(string relativePath) => File.OpenRead(FixturePath(Loop));

        protected internal override ISaveStorage OpenSaveStorage(string localFolderName) => throw new NotSupportedException();
    }
}

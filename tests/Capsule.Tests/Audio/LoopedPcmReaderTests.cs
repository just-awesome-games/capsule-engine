using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Audio;

// What a streamed voice reads, with no file and no device: the region logic alone, against a source
// whose every sample is its own frame index so a read asserts exactly which sample it came from.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class LoopedPcmReaderTests
{
    private const int Rate = 8;

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(16)]
    public void ALoopWithARegion_PlaysTheIntroOnceAndThenRepeatsTheRegion(int buffer)
    {
        // Frames 0..7, repeating 2..5 once frame 6 is reached.
        LoopedPcmReader reader = Reader(Frames(8), Region(2, 6), loop: true);
        float[] expected = [0, 1, 2, 3, 4, 5, 2, 3, 4, 5, 2, 3, 4, 5, 2, 3, 4, 5];

        Assert.Equal(expected, Read(reader, expected.Length, buffer));
    }

    // The region's own end is the boundary, not the buffer's: a repeat continues inside the span it
    // ended in, which is what makes it gapless at any buffer size.
    [Fact]
    public void ARegionsBoundary_IsCrossedInsideOneBuffer()
    {
        LoopedPcmReader reader = Reader(Frames(8), Region(2, 6), loop: true);
        float[] read = new float[8];
        float[] expected = [0, 1, 2, 3, 4, 5, 2, 3];

        Assert.Equal(8, reader.Read(read));
        Assert.Equal(expected, read);
    }

    [Fact]
    public void AVoiceThatDoesNotLoop_IgnoresTheRegionAndPlaysToTheEnd()
    {
        LoopedPcmReader reader = Reader(Frames(8), Region(2, 6), loop: false);
        float[] expected = [0, 1, 2, 3, 4, 5, 6, 7];

        Assert.Equal(expected, Read(reader, expected.Length, buffer: 3));
        Assert.Equal(0, reader.Read(new float[4]));
    }

    [Fact]
    public void ALoopWithNoRegion_RepeatsTheWholeClip()
    {
        LoopedPcmReader reader = Reader(Frames(4), AudioLoopRegion.None, loop: true);
        float[] expected = [0, 1, 2, 3, 0, 1, 2, 3, 0, 1];

        Assert.Equal(expected, Read(reader, expected.Length, buffer: 3));
    }

    // A start offset is where the first read begins; the region is unaffected by it, so a loop still
    // reaches the region's end once and repeats from its start. A start inside the region reads on
    // from it rather than rewinding first — which is the position the mixer folds a start past the
    // region's end into.
    [Theory]
    [InlineData(3, new float[] { 3, 4, 5, 2, 3, 4, 5, 2 })]
    [InlineData(4, new float[] { 4, 5, 2, 3, 4, 5, 2, 3 })]
    public void AStartFrame_IsWhereTheFirstReadBegins(long startFrame, float[] expected)
    {
        LoopedPcmReader reader = Reader(Frames(8), Region(2, 6), loop: true, startFrame);

        Assert.Equal(expected, Read(reader, expected.Length, buffer: 3));
    }

    [Fact]
    public void AStartFrameOnAVoiceThatDoesNotLoop_ReadsTheRestOfTheSourceAndEnds()
    {
        LoopedPcmReader reader = Reader(Frames(8), AudioLoopRegion.None, loop: false, startFrame: 5);
        float[] expected = [5, 6, 7];

        Assert.Equal(expected, Read(reader, expected.Length, buffer: 4));
        Assert.Equal(0, reader.Read(new float[4]));
    }

    // A region no sample ever comes out of would otherwise wrap forever inside one fill.
    [Fact]
    public void ARegionTheSourceYieldsNothingFrom_EndsRatherThanSpinning()
    {
        LoopedPcmReader reader = Reader(Frames(2), Region(4, 6), loop: true);

        Assert.Equal(2, reader.Read(new float[8]));
        Assert.Equal(0, reader.Read(new float[8]));
    }

    // Both bounds are a whole sample count over the file's own rate, so a region at no round second
    // still lands on the sample the file named.
    [Fact]
    public void ARegionInSeconds_ResolvesToTheSampleTheFileNamed()
    {
        LoopedPcmReader reader = Reader(
            Frames(1000, 44100),
            new AudioLoopRegion(441 / 44100.0, 883 / 44100.0),
            loop: true);

        float[] read = new float[884];

        Assert.Equal(884, reader.Read(read));
        Assert.Equal(882f, read[882]);
        Assert.Equal(441f, read[883]);
    }

    // Stereo: a frame is one sample per channel, and the region is measured in frames.
    [Fact]
    public void AStereoRegion_WrapsOnWholeFrames()
    {
        PcmAudio stereo = new([0f, 10f, 1f, 11f, 2f, 12f, 3f, 13f], 2, Rate);
        LoopedPcmReader reader = Reader(stereo, Region(1, 3), loop: true);
        float[] expected = [0, 10, 1, 11, 2, 12, 1, 11, 2, 12];

        Assert.Equal(expected, Read(reader, expected.Length, buffer: 4));
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(1, 16)]
    [InlineData(1, 24)]
    [InlineData(3, 32)]
    public void TheWavReader_NormalisesEveryWidthItAdmits(int tag, int bits)
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.wav", Wav(tag, bits));

        PcmAudio samples = PcmAudio.FromWav("clip.wav", "clip");
        float[] read = new float[3];
        float[] expected = [0f, 0.5f, -0.5f];

        Assert.Equal(1, samples.Channels);
        Assert.Equal(Rate, samples.SampleRate);
        Assert.Equal(3, new MemoryPcmSource(samples).Read(read));
        Assert.Equal(expected, read);
    }

    [Fact]
    public void AWavWiderThanStereo_IsRefused()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.wav", Wav(1, 16, channels: 6));

        NotSupportedException refused =
            Assert.Throws<NotSupportedException>(() => PcmAudio.FromWav("clip.wav", "surround"));

        Assert.Contains("surround", refused.Message, StringComparison.Ordinal);
    }

    private static AudioLoopRegion Region(int start, int end) => new(start / (double)Rate, end / (double)Rate);

    private static LoopedPcmReader Reader(PcmAudio samples, in AudioLoopRegion region, bool loop, long startFrame = 0)
    {
        LoopedPcmReader reader = new();
        reader.Arm(new MemoryPcmSource(samples), region, loop, startFrame);

        return reader;
    }

    // Every sample is its own frame index.
    private static PcmAudio Frames(int count, int rate = Rate)
    {
        float[] samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = i;
        }

        return new PcmAudio(samples, 1, rate);
    }

    private static float[] Read(LoopedPcmReader reader, int samples, int buffer)
    {
        List<float> read = [];
        float[] window = new float[buffer];

        while (read.Count < samples)
        {
            int filled = reader.Read(window);
            if (filled == 0)
            {
                break;
            }

            read.AddRange(window.AsSpan(0, filled));
        }

        return [.. read.Take(samples)];
    }

    // One 'fmt ' chunk and one 'data' chunk holding silence, half scale and minus half scale at the
    // width the format declares: 8-bit unsigned, 16- and 24-bit signed, or IEEE float at tag 3.
    private static byte[] Wav(int tag, int bits, int channels = 1)
    {
        int width = bits / 8;

        using MemoryStream file = new();
        using BinaryWriter writer = new(file);

        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)tag);
        writer.Write((short)channels);
        writer.Write(Rate);
        writer.Write(Rate * width * channels);
        writer.Write((short)(width * channels));
        writer.Write((short)bits);

        writer.Write("data"u8);
        writer.Write(3 * width);

        foreach (float sample in new[] { 0f, 0.5f, -0.5f })
        {
            switch (bits)
            {
                case 8:
                    writer.Write((byte)(128 + (sample * 128)));
                    break;

                case 16:
                    writer.Write((short)(sample * 32768));
                    break;

                case 24:
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((sbyte)(sample * 128));
                    break;

                default:
                    writer.Write(sample);
                    break;
            }
        }

        writer.Flush();

        return file.ToArray();
    }
}

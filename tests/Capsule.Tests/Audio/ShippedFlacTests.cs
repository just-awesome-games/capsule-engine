using System.Buffers.Binary;
using Capsule.Audio;
using Capsule.Build.Audio;
using Capsule.Runtime.Audio;
using Capsule.Tests.Build;
using Capsule.Tests.Documents;
using Capsule.Tests.Runtime;

namespace Capsule.Tests.Audio;

/// <summary>
/// A WAVE of integer samples ships as FLAC, and the host decodes the shipped file to the samples it
/// holds for the source WAVE, bit for bit. A malformed shipped file is refused naming its defect.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class ShippedFlacTests
{
    private const int Rate = 22050;

    // Two whole blocks and a short one: a sine, digital silence, both full-scale extremes, full-range
    // noise, and a block whose samples share trailing zero bits. Every subframe type and stereo
    // decorrelation the encoder chooses between is chosen somewhere in it.
    private const int Frames = (FlacEncoder.BlockSize * 2) + 1000;

    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 2)]
    [InlineData(16, 1)]
    [InlineData(16, 2)]
    [InlineData(24, 1)]
    [InlineData(24, 2)]
    [InlineData(32, 1)]
    [InlineData(32, 2)]
    [InlineData(32, 1, true)]
    public void AShippedClip_DecodesToTheSourceSamplesBitForBit(int bits, int channels, bool residualAtInt32Minimum = false)
    {
        using ToolWorkspace workspace = new();

        // [0, int.MinValue, 0, 1] predicts a residual of int.MinValue at fixed order 0, which the
        // format forbids. The encoder codes it some other way.
        byte[] samples = residualAtInt32Minimum ? [0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0, 0, 0, 1, 0, 0, 0] : Samples(bits, channels);
        byte[] source = WavFixtures.Wav(Rate, bits, channels, samples);
        workspace.Write("Assets/Audio/clip.wav", source);

        workspace.Succeed();
        PcmAudio decoded = AudioFiles.ReadResident(new ContentPlatform(Path.GetFullPath(ToolWorkspace.Out)), new AudioClip("audio/clip", ".wav", 0));

        Assert.Equal(["audio/clip.flac"], workspace.Shipped);
        AssertSameSamples(PcmAudio.FromWav(source, "audio/clip"), decoded);
    }

    // A float WAVE ships as authored, since FLAC carries no float samples, and its member says so.
    [Fact]
    public void AFloatClip_ShipsAsAuthored()
    {
        using ToolWorkspace workspace = new();
        byte[] source = WavFixtures.Wav(Rate, 32, 1, new byte[400], formatTag: 3);
        workspace.Write("Assets/Audio/hum.wav", source);

        workspace.Succeed();
        AudioClip clip = new("audio/hum", ".wav", 0, default, floatSamples: true);

        Assert.Equal(["audio/hum.wav"], workspace.Shipped);
        Assert.Contains("AudioClip HumSound => new global::Capsule.Audio.AudioClip(\"audio/hum\", \".wav\", ", workspace.Generated, StringComparison.Ordinal);
        Assert.Contains("D, default, floatSamples: true);", workspace.Generated, StringComparison.Ordinal);
        AssertSameSamples(PcmAudio.FromWav(source, "audio/hum"), AudioFiles.ReadResident(new ContentPlatform(Path.GetFullPath(ToolWorkspace.Out)), clip));
    }

    // Every refusal names the clip, the defect and the fix, and none reads past the file.
    [Theory]
    [InlineData("not-flac", "is no FLAC stream")]
    [InlineData("no-streaminfo", "opens with no FLAC STREAMINFO block")]
    [InlineData("truncated", "the file is truncated")]
    [InlineData("damaged", "CRC does not match its bytes")]
    [InlineData("more-than-declared", "more samples than its FLAC STREAMINFO block declares")]
    public void AMalformedShippedFlac_IsRefusedNamingTheDefect(string defect, string expected)
    {
        using MemoryStream encoded = new();
        FlacEncoder.Encode(Samples(16, 2), 2, 16, Rate, encoded);
        byte[] file = encoded.ToArray();

        switch (defect)
        {
            case "not-flac":
                file[0] = (byte)'R';
                break;

            case "no-streaminfo":
                file[4] = 0x81;
                break;

            case "truncated":
                file = file[..(file.Length / 2)];
                break;

            case "damaged":
                file[^1] ^= 1;
                break;

            default:
                // STREAMINFO's sample count, whose low 32 bits close its 36.
                BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8 + 14), 1);
                break;
        }

        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => FlacDecoder.Decode(file, "audio/broken"));

        Assert.Contains("'audio/broken'", refused.Message, StringComparison.Ordinal);
        Assert.Contains(expected, refused.Message, StringComparison.Ordinal);
        Assert.EndsWith("Rebuild the game to ship it again.", refused.Message, StringComparison.Ordinal);
    }

    private static void AssertSameSamples(PcmAudio expected, PcmAudio actual)
    {
        Assert.Equal(expected.Channels, actual.Channels);
        Assert.Equal(expected.SampleRate, actual.SampleRate);
        Assert.Equal(expected.Pcm16, actual.Pcm16);
        Assert.Equal(expected.Float, actual.Float);
    }

    // Interleaved samples at the given width as a WAVE 'data' chunk holds them, 8-bit ones unsigned.
    private static byte[] Samples(int bits, int channels)
    {
        long largest = (1L << (bits - 1)) - 1;
        int width = bits / 8;
        byte[] data = new byte[Frames * channels * width];
        uint noise = 12345;

        for (int i = 0; i < Frames; i++)
        {
            long sine = (long)(largest * 0.6 * Math.Sin(i * 0.05));
            noise = (noise * 1664525) + 1013904223;
            long value = i switch
            {
                < 3000 => sine,
                < 4500 => 0,
                < 4600 => largest,
                < 4700 => -largest - 1,
                < 7000 => (int)noise >> (32 - bits),
                < FlacEncoder.BlockSize * 2 => sine + (((int)noise >> (32 - bits)) / 16),
                _ => (sine >> 4) << 4,
            };

            for (int channel = 0; channel < channels; channel++)
            {
                long sample = channel == 0 ? value : (value / 4 * 3) + (i & 7);
                Span<byte> at = data.AsSpan(((i * channels) + channel) * width, width);
                switch (bits)
                {
                    case 8:
                        at[0] = (byte)(sample + 128);
                        break;

                    case 16:
                        BinaryPrimitives.WriteInt16LittleEndian(at, (short)sample);
                        break;

                    case 24:
                        at[0] = (byte)sample;
                        at[1] = (byte)(sample >> 8);
                        at[2] = (byte)(sample >> 16);
                        break;

                    default:
                        BinaryPrimitives.WriteInt32LittleEndian(at, (int)sample);
                        break;
                }
            }
        }

        return data;
    }
}

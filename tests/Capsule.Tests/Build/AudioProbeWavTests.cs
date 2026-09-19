using Capsule.Audio;
using Capsule.Build.Audio;
using Capsule.Tests.Documents;
using static Capsule.Tests.Build.AudioProbeFixtures;

namespace Capsule.Tests.Build;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class AudioProbeWavTests
{
    [Theory]
    [InlineData(1, 16, 22050, 1764, 0.08)]
    [InlineData(2, 16, 44100, 22050, 0.5)]
    [InlineData(1, 8, 8000, 4000, 0.5)]
    public void AWavsDuration_IsItsDataChunkOverItsFrameSizeAndRate(
        int channels,
        int bits,
        int rate,
        int frames,
        double expected)
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.wav", Wav(channels, bits, rate, frames));

        AudioProbe.Measurement measured = AudioProbe.Measure("clip.wav");

        Assert.Equal(expected, measured.DurationSeconds, 9);
        Assert.Equal(AudioLoopRegion.None, measured.Loop);
    }

    // The chunk walk skips whatever it does not read, whether it precedes 'data' or follows it.
    [Fact]
    public void AWavCarryingOtherChunks_IsMeasuredFromTheOnesThatMatter()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.wav", Wav(1, 16, 22050, 1764, padded: true));

        Assert.Equal(0.08, AudioProbe.Measure("clip.wav").DurationSeconds, 9);
    }

    // A sampler chunk's loop end is the last sample sounded, so the region ends one past it.
    [Fact]
    public void AWavsLoopRegion_IsTheFirstSampleLoopOfItsSamplerChunk()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.wav", Wav(1, 16, 22050, 1764, loop: (441, 881)));

        AudioLoopRegion region = AudioProbe.Measure("clip.wav").Loop;

        Assert.Equal(441 / 22050.0, region.StartSeconds, 12);
        Assert.Equal(882 / 22050.0, region.EndSeconds, 12);
    }

    [Fact]
    public void AWavCapsuleCannotDecode_IsRefused()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        byte[] file = Wav(1, 16, 22050, 8);

        // The format tag, at the head of the 'fmt ' chunk: 0x11 is IMA ADPCM.
        file[20] = 0x11;
        File.WriteAllBytes("clip.wav", file);

        AudioFormatException refused =
            Assert.Throws<AudioFormatException>(() => AudioProbe.Measure("clip.wav"));

        Assert.Contains("PCM", refused.Message, StringComparison.Ordinal);
    }
}

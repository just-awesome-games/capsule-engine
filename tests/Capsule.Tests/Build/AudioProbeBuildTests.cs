using Capsule.Tests.Documents;
using static Capsule.Tests.Build.AudioProbeFixtures;

namespace Capsule.Tests.Build;

/// <summary>
/// Every clip reaches the game as a typed member carrying the duration and loop region the build
/// measured, and a clip the build cannot measure fails it naming the file.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class AudioProbeBuildTests
{
    // Whatever the defect, the build names the source that failed and what is wrong with it, and
    // stamps nothing: a game never compiles against half a registry. A truncated comment header is
    // a malformed container rather than a clip without a region, since reading the tags that
    // happen to precede the cut would ship a loop nobody authored; a comment declaring more bytes
    // than its own packet holds is no licence to read the setup packet laced behind it either.
    [Theory]
    [InlineData("unfinished-comment-header", ".ogg", "truncated")]
    [InlineData("comment-overrunning-its-packet", ".ogg", "truncated")]
    [InlineData("empty", ".wav", "truncated")]
    [InlineData("loop-past-the-end", ".wav", "[441, 2001) in 1764 sample(s)")]
    public void AMalformedSource_FailsTheBuildNamingTheFileAndTheDefect(
        string defect,
        string extension,
        string expected)
    {
        using ToolWorkspace workspace = new();
        string source = workspace.Write("Assets/Audio/music/theme" + extension, Malformed(defect));
        workspace.Write("Assets/Audio/good.wav", Wav(1, 16, 22050, 1764));

        string errors = workspace.Fail();

        Assert.Contains(source + ":", errors, StringComparison.Ordinal);
        Assert.Contains(expected, errors, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryClip_IsDeclaredWithItsDurationAndLoopRegion()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Audio/Steps/Stone.wav", Wav(1, 16, 22050, 1764));
        workspace.Write("Assets/Audio/music/theme.wav", Wav(1, 16, 22050, 2205, loop: (441, 1764)));

        workspace.Succeed();

        string generated = workspace.Generated;
        Assert.Contains("AudioClip StoneSound => new global::Capsule.Audio.AudioClip(\"audio/steps/stone\", \".wav\", 0.08D);", generated, StringComparison.Ordinal);
        Assert.Contains(
            "AudioClip ThemeSound => new global::Capsule.Audio.AudioClip(\"audio/music/theme\", \".wav\", 0.1D, new global::Capsule.Audio.AudioLoopRegion(0.02D, ",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("0.1 seconds long, looping 0.02 to ", generated, StringComparison.Ordinal);
        Assert.Equal(["audio/music/theme.wav", "audio/steps/stone.wav"], workspace.Shipped);
    }
}

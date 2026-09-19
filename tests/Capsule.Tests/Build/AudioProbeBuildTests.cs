using Capsule.Build;
using Capsule.Build.Audio;
using Capsule.Tests.Documents;
using static Capsule.Tests.Build.AudioProbeFixtures;

namespace Capsule.Tests.Build;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class AudioProbeBuildTests
{
    // Whatever the defect, the hook names the source that failed and what is wrong with it, and
    // writes nothing: a game never compiles against half a registry. A truncated comment header is
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
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("music");
        string source = "music/theme" + extension;
        File.WriteAllBytes(source, Malformed(defect));
        File.WriteAllBytes("good.wav", Wav(1, 16, 22050, 1764));

        StringWriter error = new();
        int exitCode = AudioTool.Emit(
            [new DocumentSource("music/theme", source), new DocumentSource("good", "good.wav")],
            "CapsuleAssets.Audio.g.cs",
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains(source + ":", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(expected, error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("CapsuleAssets.Audio.g.cs"));
    }
}

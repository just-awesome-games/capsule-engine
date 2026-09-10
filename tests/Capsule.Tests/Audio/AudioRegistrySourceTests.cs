using Capsule.Audio;
using Capsule.Build.Audio;

namespace Capsule.Tests.Audio;

public sealed class AudioRegistrySourceTests
{
    [Fact]
    public void ADirectoryUnderTheAudioRoot_BecomesANestedClassAndItsOwnSet()
    {
        string generated = AudioRegistrySource.Render(
        [
            new AudioSourceClip("steps/stone", ".wav", 0.08),
            new AudioSourceClip("music/theme", ".ogg", 92.5),
            new AudioSourceClip("hit", ".wav", 0.25),
        ]);

        Assert.Contains("public static partial class CapsuleAssets", generated, StringComparison.Ordinal);
        Assert.Contains("public static class Audio", generated, StringComparison.Ordinal);
        Assert.Contains("public static class Steps", generated, StringComparison.Ordinal);
        Assert.Contains("AudioClip Stone => new global::Capsule.Audio.AudioClip(\"steps/stone\", \".wav\", 0.08D);", generated, StringComparison.Ordinal);
        Assert.Contains("AudioClip Theme => new global::Capsule.Audio.AudioClip(\"music/theme\", \".ogg\", 92.5D);", generated, StringComparison.Ordinal);

        // Every class carries every clip beneath it; a nested one carries only its own.
        string dense = Dense(generated);
        Assert.Contains("\nHit,\n", dense, StringComparison.Ordinal);
        Assert.Contains("\nMusic.Theme,\n", dense, StringComparison.Ordinal);
        Assert.Contains("\nSteps.Stone,\n", dense, StringComparison.Ordinal);
        Assert.Contains("\nStone,\n", dense, StringComparison.Ordinal);
    }

    // A clip the build read no region from renders as it did before regions existed: the fourth
    // argument is the constructor's own default and is never spelt out.
    [Fact]
    public void AClipCarryingALoopRegion_DeclaresIt()
    {
        string generated = AudioRegistrySource.Render(
        [
            new AudioSourceClip("music/theme", ".ogg", 92.5, new AudioLoopRegion(4.25, 92.5)),
            new AudioSourceClip("hit", ".wav", 0.25),
        ]);

        Assert.Contains(
            "AudioClip Theme => new global::Capsule.Audio.AudioClip(\"music/theme\", \".ogg\", 92.5D, new global::Capsule.Audio.AudioLoopRegion(4.25D, 92.5D));",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("92.5 seconds long, looping 4.25 to 92.5 seconds.", generated, StringComparison.Ordinal);
        Assert.Contains("AudioClip Hit => new global::Capsule.Audio.AudioClip(\"hit\", \".wav\", 0.25D);", generated, StringComparison.Ordinal);
    }

    // The sources arrive in whatever order the build collected them; the file must not.
    [Fact]
    public void TheRenderedFile_IsTheSameWhateverOrderTheSourcesArriveIn()
    {
        AudioSourceClip stone = new("steps/stone", ".wav", 0.08);
        AudioSourceClip theme = new("music/theme", ".ogg", 92.5);

        Assert.Equal(
            AudioRegistrySource.Render([stone, theme]),
            AudioRegistrySource.Render([theme, stone]));
    }

    [Theory]
    [InlineData("audio", "a class of that name")]
    [InlineData("all", "the set member")]
    [InlineData("steps/all", "the set member")]
    [InlineData("01-stone", "no C# name")]
    [InlineData("steps/hey there", "no C# name")]
    public void AKeyTheGeneratedClassesCannotDeclare_IsRefused(string key, string because)
    {
        AudioNames declared = new();

        AudioFormatException refused = Assert.Throws<AudioFormatException>(() => declared.Declare(key));

        Assert.Contains(because, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoKeysThatAreOneCsharpNameInOneDirectory_AreRefused()
    {
        AudioNames declared = new();
        declared.Declare("steps/foot-step");

        Assert.Throws<AudioFormatException>(() => declared.Declare("steps/foot_step"));

        // A directory two clips share is one class, not a collision.
        declared.Declare("steps/other");
    }

    private static string Dense(string generated) =>
        generated.Replace("\r\n", "\n", StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
}

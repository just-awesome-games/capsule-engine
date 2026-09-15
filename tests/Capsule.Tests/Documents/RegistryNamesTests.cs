using Capsule.Build.Audio;

namespace Capsule.Tests.Documents;

// Every collision C# would refuse, caught as the registry is rendered and named against the key
// that would have caused it.
public sealed class RegistryNamesTests
{
    [Theory]
    [InlineData("audio", "class of that name")]
    [InlineData("all", "the set member")]
    [InlineData("steps/all", "the set member")]
    [InlineData("01-stone", "no C# name")]
    [InlineData("steps/hey there", "no C# name")]
    public void AKeyTheGeneratedClassesCannotDeclare_IsRefused(string key, string because)
    {
        Assert.Null(Render([key], out string? refused));
        Assert.Contains(because, refused!, StringComparison.Ordinal);
    }

    // A directory takes no name a key's own last segment would collide with.
    [Fact]
    public void ADirectoryNamedLikeAGeneratedMember_IsDeclared() =>
        Assert.NotNull(Render(["steps/all-clear"], out _));

    [Fact]
    public void TwoKeysThatAreOneCsharpNameInOneDirectory_AreRefused()
    {
        Assert.Null(Render(["steps/foot-step", "steps/foot_step"], out string? refused));
        Assert.Contains("foot-step", refused!, StringComparison.Ordinal);

        // A directory two clips share is one class, not a collision.
        Assert.NotNull(Render(["steps/foot-step", "steps/other"], out _));
    }

    // A leaf and the directory of another key are one name too, whichever is declared first.
    [Theory]
    [InlineData("steps", "steps/stone")]
    [InlineData("steps/stone", "steps")]
    public void ALeafAndADirectoryOfOneName_AreRefused(string first, string second) =>
        Assert.Null(Render([first, second], out _));

    private static string? Render(string[] keys, out string? because) =>
        AudioRegistrySource.Render(
            [.. keys.Select(static key => new AudioSourceClip(key, ".wav", 1, default))],
            out _,
            out because);
}

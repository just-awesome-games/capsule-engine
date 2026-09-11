using Capsule.Build;
using Capsule.Build.Audio;
using Capsule.Build.Sprites;

namespace Capsule.Tests.Documents;

// Every collision C# would refuse, caught against the source that would have caused it, in both
// configurations the generated registries ask for.
public sealed class RegistryNamesTests
{
    [Theory]
    [InlineData("audio", "a class of that name")]
    [InlineData("all", "the set member")]
    [InlineData("steps/all", "the set member")]
    [InlineData("01-stone", "no C# name")]
    [InlineData("steps/hey there", "no C# name")]
    public void AKeyTheGeneratedClassesCannotDeclare_IsRefused(string key, string because)
    {
        RegistryNames declared = Clips();

        Assert.Contains(because, declared.Declare(key)!, StringComparison.Ordinal);
    }

    // A sheet declares Frames and Clips inside its own class; a directory declares neither, and
    // neither does an enclosing set member, since a sheet carries none.
    [Theory]
    [InlineData("frames", "a sheet declares")]
    [InlineData("actors/clips", "a sheet declares")]
    public void ASheetKeyTakingAGeneratedClassesName_IsRefused(string key, string because)
    {
        RegistryNames declared = Sheets();

        Assert.Contains(because, declared.Declare(key)!, StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectoryNamedAfterAGeneratedClass_IsDeclared()
    {
        Assert.Null(Sheets().Declare("frames/idle"));
        Assert.Null(Clips().Declare("steps/all-clear"));
    }

    [Fact]
    public void TwoKeysThatAreOneCsharpNameInOneDirectory_AreRefused()
    {
        RegistryNames declared = Clips();

        Assert.Null(declared.Declare("steps/foot-step"));
        Assert.Contains("foot-step", declared.Declare("steps/foot_step")!, StringComparison.Ordinal);

        // A directory two clips share is one class, not a collision.
        Assert.Null(declared.Declare("steps/other"));
    }

    // A leaf and the directory of another key are one name too.
    [Fact]
    public void ALeafAndADirectoryOfOneName_AreRefused()
    {
        RegistryNames declared = Clips();

        Assert.Null(declared.Declare("steps"));
        Assert.NotNull(declared.Declare("steps/stone"));

        RegistryNames other = Clips();

        Assert.Null(other.Declare("steps/stone"));
        Assert.NotNull(other.Declare("steps"));
    }

    private static RegistryNames Clips() => new(AudioRegistrySource.RegistryClass, AudioRegistrySource.Reserved);

    private static RegistryNames Sheets() => new(SpriteRegistrySource.RegistryClass, SpriteRegistrySource.Reserved);
}

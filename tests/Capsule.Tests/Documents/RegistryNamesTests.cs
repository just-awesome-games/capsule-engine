using Capsule.Build;
using Capsule.Build.Audio;

namespace Capsule.Tests.Documents;

// Every collision C# would refuse, caught against the source that would have caused it.
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

    // A directory takes no name the generated members reserve, since only a leaf becomes one.
    [Fact]
    public void ADirectoryNamedAfterAGeneratedMember_IsDeclared() =>
        Assert.Null(Clips().Declare("steps/all-clear"));

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
}

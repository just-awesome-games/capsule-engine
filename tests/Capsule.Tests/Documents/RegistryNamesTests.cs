using Capsule.Build.Registry;

namespace Capsule.Tests.Documents;

// Every collision C# would refuse, caught as CapsuleAssets is built and named against the file that
// would have caused it.
public sealed class RegistryNamesTests
{
    // A member cannot take the name of the class it is declared on.
    [Theory]
    [InlineData("hero-texture/hero", "HeroTexture")]
    [InlineData("capsule-assets", "CapsuleAssets")]
    public void AMemberNamedAfterItsFoldersClass_IsRefused(string key, string name) =>
        Assert.Contains("inside a generated class of that name", Refused((key, name))!, StringComparison.Ordinal);

    // A file may share its folder's name, since its member ends in its type.
    [Fact]
    public void AFileNamedLikeItsFolder_IsDeclared() =>
        Assert.Null(Refused(("player/player", "PlayerTexture"), ("player/idle", "IdleTexture")));

    [Fact]
    public void TwoNamesThatAreOneCsharpNameInOneFolder_AreRefused()
    {
        Assert.Contains("foot-step", Refused(("steps/foot-step", "FootStepSound"), ("steps/foot_step", "FootStepSound"))!, StringComparison.Ordinal);

        // A folder two files share is one class, not a collision.
        Assert.Null(Refused(("steps/foot-step", "FootStepSound"), ("steps/other", "OtherSound")));
    }

    // A member and a nested folder of one name are one name too, whichever is declared first:
    // steps.wav is StepsSound, as a folder named steps-sound is.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMemberAndAFolderOfOneName_AreRefused(bool memberFirst)
    {
        (string, string) member = ("steps", "StepsSound");
        (string, string) nested = ("steps-sound/stone", "StoneSound");

        Assert.NotNull(memberFirst ? Refused(member, nested) : Refused(nested, member));
    }

    // The first refusal adding every file in order meets, or null when all were declared.
    private static string? Refused(params (string Key, string Name)[] files)
    {
        RegistryFolder root = new("CapsuleAssets", string.Empty);

        return files
            .Select(file => root.Add(file.Key, file.Name, file.Key, static (_, _, _) => { }))
            .FirstOrDefault(static because => because is not null);
    }
}

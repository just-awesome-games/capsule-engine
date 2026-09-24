using Capsule.Generators;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// An asset's type is its extension, its key is its path under <c>Assets/</c> normalized segment by
/// segment, and it ships at that path. The engine dictates neither how a game organizes what it
/// authors nor how it spells it: one asset has one key, one member and one shipped path.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class AssetKeyTests
{
    [Theory]
    [InlineData("Enemies/Bat", "enemies/bat")]
    [InlineData("enemies/Bat", "enemies/bat")]
    [InlineData("enemies/bat", "enemies/bat")]
    [InlineData("Stage1/Room01", "stage-1/room-01")]
    [InlineData("stage1/room01", "stage-1/room-01")]
    [InlineData("stage-1/room-01", "stage-1/room-01")]
    [InlineData("Foot_Step", "foot-step")]
    [InlineData("BodyText", "body-text")]
    public void EverySpellingOfAPath_KeysTheSame(string authored, string key)
    {
        Assert.Equal(key, TypeNaming.NormalizeKey(authored, out _));

        // Idempotent: the key of a key is that key, so an already-normalized game keeps its paths.
        Assert.Equal(key, TypeNaming.NormalizeKey(key, out _));
    }

    [Theory]
    [InlineData("01-intro/hero", "01-intro")]
    [InlineData("enemies/bat.small", "bat.small")]
    public void ASegmentThatIsNoIdentifier_HasNoKey(string authored, string rejected)
    {
        Assert.Null(TypeNaming.NormalizeKey(authored, out string? named));
        Assert.Equal(rejected, named);
    }

    // Organized by type or by object, a file ships at its path and is a member of its folder's class
    // named for its file and its type. A player's texture, sheet and sound share a folder and a name.
    [Fact]
    public void AFile_ShipsAtItsPathAndIsNamedForItsFileAndType()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Textures/Enemies/Bat.png", string.Empty);
        workspace.Write("Assets/Player/player.png", string.Empty);
        workspace.Write(
            "Assets/Player/player.sheet.json",
            """{ "formatVersion": 1, "texture": "player/player.png", "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ] }""");
        workspace.Write("Assets/Player/Step_Soft.wav", AudioProbeFixtures.Wav(1, 16, 22050, 441));
        workspace.Write("Assets/Player/notes.txt", "a file of no type the build reads");

        workspace.Succeed();

        Assert.Equal(["player/player.png", "player/step-soft.wav", "textures/enemies/bat.png"], workspace.Shipped);
        Assert.Contains("TextureHandle PlayerTexture => new global::Capsule.Assets.TextureHandle(\"player/player\", \".png\");", workspace.Generated, StringComparison.Ordinal);
        Assert.Contains("public static class PlayerSheet", workspace.Generated, StringComparison.Ordinal);
        Assert.Contains("AudioClip StepSoftSound =>", workspace.Generated, StringComparison.Ordinal);
        Assert.Contains("TextureHandle BatTexture =>", workspace.Generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeyPass_NamesTheSourceAndTheSegmentItCannotName()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Textures/01-intro/Hero.png", string.Empty);

        string errors = workspace.Fail();

        Assert.Contains("Assets/Textures/01-intro/Hero.png", errors, StringComparison.Ordinal);
        Assert.Contains("\"01-intro\" is no C# name", errors, StringComparison.Ordinal);
    }

    // Two spellings of one path are one asset, as are two formats of one sound.
    [Theory]
    [InlineData("Assets/Textures/Foot_Step.png", "Assets/Textures/foot-step.png")]
    [InlineData("Assets/Audio/hit.ogg", "Assets/Audio/hit.wav")]
    public void TwoSpellingsOfOneKey_FailTheBuildNamingBoth(string first, string second)
    {
        using ToolWorkspace workspace = new();
        workspace.Write(first, string.Empty);
        workspace.Write(second, string.Empty);

        string errors = workspace.Fail();

        Assert.Contains(first, errors, StringComparison.Ordinal);
        Assert.Contains(second, errors, StringComparison.Ordinal);
    }
}

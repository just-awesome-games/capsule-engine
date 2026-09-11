using Capsule.Build;
using Capsule.Build.Keys;
using Capsule.Generators;

namespace Capsule.Tests.Generators;

/// <summary>
/// A key is the authored path normalized segment by segment, so the engine dictates no spelling
/// below a domain root: whatever a game called a directory or a file, one asset has one key, one
/// identifier and one shipped path.
/// </summary>
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

    [Fact]
    public void TheKeyPass_NamesTheSourceAndTheSegmentItCannotName()
    {
        using Workspace workspace = new();
        StringWriter error = new();

        int exitCode = Derive(workspace, error, "textures|01-intro/Hero|.png|Assets/Textures/01-intro/Hero.png");

        Assert.Equal(1, exitCode);
        Assert.Contains("Assets/Textures/01-intro/Hero.png", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("01-intro", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoSpellingsOfOneKey_FailTheBuildNamingBoth()
    {
        using Workspace workspace = new();
        StringWriter error = new();

        int exitCode = Derive(
            workspace,
            error,
            "textures|Enemies/Bat|.png|Assets/Textures/Enemies/Bat.png",
            "textures|enemies/bat|.png|Assets/Textures/enemies/bat.png");

        Assert.Equal(1, exitCode);
        Assert.Contains("Assets/Textures/Enemies/Bat.png", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Assets/Textures/enemies/bat.png", error.ToString(), StringComparison.Ordinal);
    }

    // The same key in two domains is two assets, as two spellings of one are one.
    [Fact]
    public void TheKeyPass_ShipsEachAssetAtItsKey()
    {
        using Workspace workspace = new();

        int exitCode = Derive(
            workspace,
            TextWriter.Null,
            "textures|Enemies/Bat|.png|Assets/Textures/Enemies/Bat.png",
            "audio|Music/Main_Theme|.ogg|Assets/Audio/Music/Main_Theme.ogg",
            "scenes|Stage1/Room01||Assets/Scenes/Stage1/Room01.scene.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ["assets/textures/enemies/bat.png|Assets/Textures/Enemies/Bat.png", "assets/audio/music/main-theme.ogg|Assets/Audio/Music/Main_Theme.ogg"],
            workspace.Read("shipped-assets.txt"));
        Assert.Equal(
            ["assets/scenes/stage-1/room-01.scene.json|derived/stage-1/room-01.scene.json"],
            workspace.Read("scene-content.txt"));
    }

    private static int Derive(Workspace workspace, TextWriter error, params string[] requests)
    {
        string requestFile = Path.Combine(workspace.Root, "requests.txt");
        File.WriteAllLines(requestFile, requests);

        List<KeyedAsset> keyed = [];
        if (KeyTool.Derive(BuildRequests.Read(requestFile).Assets, keyed, error) > 0)
        {
            return 1;
        }

        KeyTool.WriteManifests(keyed, workspace.Root, "derived/");

        return 0;
    }

    private sealed class Workspace : IDisposable
    {
        internal Workspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "capsule-keys-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        internal string[] Read(string name) => File.ReadAllLines(Path.Combine(Root, name));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

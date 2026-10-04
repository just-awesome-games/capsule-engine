using System.IO.Compression;
using Capsule.Build.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class NativeSceneToolTests
{
    private const string Authored = SceneDocumentFixtures.AuthoredTileMapAndPlayer;

    private const string Shipped = ToolWorkspace.Out + "/assets/scenes/";

    // A gzip header with a timestamp would make every build of an unchanged document new bytes. An
    // editor's "$schema" stays in the authored file.
    [Fact]
    public void ADocument_ShipsCompactAndGzippedAtItsKey_AndEveryBuildWritesTheSameBytes()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/hall.scene.json", Authored.Replace("{ \"entities\"", "{ \"$schema\": \"scene.schema.json\", \"entities\"", StringComparison.Ordinal));
        string path = Shipped + "hall.scene.json.gz";

        workspace.Succeed();
        byte[] shipped = File.ReadAllBytes(path);
        File.Delete(path);
        workspace.Succeed();

        Assert.Equal(shipped, File.ReadAllBytes(path));
        Assert.Equal([0x1f, 0x8b, 0, 0, 0, 0], [shipped[0], shipped[1], .. shipped[4..8]]);
        using StreamReader inflated = new(new GZipStream(new MemoryStream(shipped), CompressionMode.Decompress));
        string emitted = inflated.ReadToEnd();
        SceneDocument derived = SceneDocument.Parse(emitted);
        Assert.Equal(derived.ToJson(), emitted);
        Assert.NotEqual(Authored, emitted);
        Assert.DoesNotContain("$schema", emitted, StringComparison.Ordinal);
        Assert.Equal(2, derived.Entries[0].Members?.GetProperty("width").GetInt32());
        Assert.Equal("player", derived.Entries[1].Type);
    }

    // An imported document ships at the key of the path its importer wrote it to.
    [Fact]
    public void AnImportedDocument_ShipsAtItsPath()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Upper_Halls/Hall.note", Authored);
        workspace.Configure = static build => build.AddImporter(new NoteImporter());

        workspace.Succeed();

        Assert.Equal("player", Load(ToolWorkspace.Out + "/assets/upper-halls/hall.scene.json.gz").Entries[1].Type);
    }

    [Fact]
    public void AMalformedDocument_FailsByNameAndTheOthersStillDerive()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/hall.scene.json", Authored);
        workspace.Write("Assets/Scenes/broken.scene.json", """{ "entities": [ { "id": 1, "x": 0, "y": 0 } ] }""");

        string errors = workspace.Fail();

        Assert.Contains("Assets/Scenes/broken.scene.json", errors, StringComparison.Ordinal);
        Assert.Contains("has no type", errors, StringComparison.Ordinal);
        Assert.False(File.Exists(Shipped + "broken.scene.json.gz"));
        Assert.True(File.Exists(Shipped + "hall.scene.json.gz"));
    }

    // Every shipped document reaches the generator with its baseScene, and its key reaches the game as a SceneKey.
    [Fact]
    public void EveryDocument_IsHandedToTheGeneratorAndNamedInCode()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/Dev/Room_Wide.scene.json", """{"baseScene": "playable-room", "entities": []}""");
        workspace.Write("Assets/Scenes/room.scene.json", Authored);

        workspace.Succeed();

        string generated = workspace.Generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Matches(
            """\[CapsuleGeneratedSceneDocument\(Key = "scenes/dev/room-wide", BaseScene = "playable-room"\)\]\n +public static global::Capsule\.Scenes\.SceneKey RoomWideScene => new global::Capsule\.Scenes\.SceneKey\("scenes/dev/room-wide"\);""",
            generated);
        Assert.Matches(
            """\[CapsuleGeneratedSceneDocument\(Key = "scenes/room"\)\]\n +public static global::Capsule\.Scenes\.SceneKey RoomScene => new global::Capsule\.Scenes\.SceneKey\("scenes/room"\);""",
            generated);
    }

    // A key that cannot be a member where it lands is refused by the build, never left to fail as a
    // C# error in generated code.
    [Theory]
    [InlineData("already declared", "Assets/halls.scene.json", "Assets/halls-scene/hall.scene.json")]
    [InlineData("inside a generated class of that name", "Assets/room-scene/room.scene.json")]
    public void ADocumentKeyNoMemberCanTake_FailsTheBuildNamingTheDocument(string because, params string[] documents)
    {
        using ToolWorkspace workspace = new();
        foreach (string document in documents)
        {
            workspace.Write(document, """{"entities": []}""");
        }

        string errors = workspace.Fail();

        Assert.Contains(because, errors, StringComparison.Ordinal);
        Assert.Contains(documents[^1], errors, StringComparison.Ordinal);
    }

    private static SceneDocument Load(string path)
    {
        using FileStream shipped = File.OpenRead(path);

        return ShippedSceneDocument.Read(shipped);
    }
}

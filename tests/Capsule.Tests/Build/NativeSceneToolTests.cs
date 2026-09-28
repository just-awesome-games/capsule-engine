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
        workspace.Write("Assets/Scenes/hall.scene.json", Authored.Replace("{ \"formatVersion\"", "{ \"$schema\": \"scene.schema.json\", \"formatVersion\"", StringComparison.Ordinal));
        string path = Shipped + "hall.scene.json.gz";

        workspace.Succeed();
        byte[] shipped = File.ReadAllBytes(path);
        File.Delete(path);
        workspace.Succeed();

        Assert.Equal(shipped, File.ReadAllBytes(path));
        Assert.Equal([0x1f, 0x8b, 0, 0, 0, 0], [shipped[0], shipped[1], .. shipped[4..8]]);
        using StreamReader inflated = new(new GZipStream(new MemoryStream(shipped), CompressionMode.Decompress));
        string emitted = inflated.ReadToEnd();
        SceneDocument derived = SceneDocumentFile.Parse(emitted);
        Assert.Equal(SceneDocumentFile.ToJson(derived, compact: true), emitted);
        Assert.NotEqual(Authored, emitted);
        Assert.DoesNotContain("$schema", emitted, StringComparison.Ordinal);
        Assert.Equal(2, derived.Entries[0].TileMap!.Value.Grid.Width);
        Assert.Equal("player", derived.Entries[1].Entity!.Value.Type);
    }

    // A texture is reached by its key, so the derived document names the path the build ships it at
    // however the document spelled it.
    [Theory]
    [InlineData("Terrain/Cave_Wall.png")]
    [InlineData("terrain/cave-wall.png")]
    public void ATileMapTexture_IsReEmittedAsItsKey(string spelled)
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/hall.scene.json", Authored.Replace("\"terrain.png\"", $"\"{spelled}\"", StringComparison.Ordinal));

        workspace.Succeed();

        Assert.Equal("terrain/cave-wall", Load(Shipped + "hall.scene.json.gz").Entries[0].TileMap!.Value.Grid.Texture?.Name);
    }

    [Fact]
    public void AnUnstampedDocument_IsStampedWithItsSourcePath()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/rooms/hall.scene.json", Authored);

        workspace.Succeed();

        SceneDocument derived = Load(Shipped + "rooms/hall.scene.json.gz");
        Assert.Equal(SceneStep.ToolName, derived.Source?.Tool);
        Assert.Equal("Assets/Scenes/rooms/hall.scene.json", derived.Source?.Path);
    }

    // An imported document arrives stamped with the file a person edited. That provenance is kept, and
    // the document ships at the path its importer wrote it to.
    [Fact]
    public void AnImportedDocument_KeepsItsSourceBlockAndShipsAtItsPath()
    {
        using ToolWorkspace workspace = new();
        SceneDocument stamped = new(
            SceneDocumentFile.Parse(Authored).Entries.ToArray(),
            3,
            new SceneDocumentSource("editor", "Assets/Upper_Halls/Hall.note", new string('a', 64)));
        workspace.Write("Assets/Upper_Halls/Hall.note", SceneDocumentFile.ToJson(stamped));
        workspace.Configure = static build => build.AddImporter(new NoteImporter());

        workspace.Succeed();

        Assert.Equal(stamped.Source, Load(ToolWorkspace.Out + "/assets/upper-halls/hall.scene.json.gz").Source);
    }

    [Fact]
    public void AMalformedDocument_FailsByNameAndTheOthersStillDerive()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/hall.scene.json", Authored);
        workspace.Write("Assets/Scenes/broken.scene.json", """{ "formatVersion": 7, "entities": [ { "id": 1, "type": "tile-map", "x": 0, "y": 0 } ], "nextEntityId": 2 }""");

        string errors = workspace.Fail();

        Assert.Contains("Assets/Scenes/broken.scene.json", errors, StringComparison.Ordinal);
        Assert.Contains("declares no properties", errors, StringComparison.Ordinal);
        Assert.False(File.Exists(Shipped + "broken.scene.json.gz"));
        Assert.True(File.Exists(Shipped + "hall.scene.json.gz"));
    }

    [Fact]
    public void ADocumentWhoseTileSizeIsNotTheDeclaredOne_Fails()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/hall.scene.json", Authored);
        workspace.Configure = static build => build.WithTileSize(8);

        Assert.Contains("Assets/Scenes/hall.scene.json", workspace.Fail(), StringComparison.Ordinal);
        Assert.False(File.Exists(Shipped + "hall.scene.json.gz"));
    }

    // Every shipped document reaches the generator with its baseScene, camera and game entries, and its
    // key reaches the game as a SceneKey.
    [Fact]
    public void EveryDocument_IsHandedToTheGeneratorAndNamedInCode()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/Dev/Room_Wide.scene.json", """{"formatVersion": 7, "baseScene": "playable-room", "camera": "follow", "entities": [], "nextEntityId": 1}""");
        workspace.Write("Assets/Scenes/room.scene.json", Authored);

        workspace.Succeed();

        string generated = workspace.Generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Matches(
            """\[CapsuleGeneratedSceneDocument\(Key = "scenes/dev/room-wide", Path = "[^"]*Assets/Scenes/Dev/Room_Wide\.scene\.json", BaseScene = "playable-room", Camera = "follow"\)\]\n +public static global::Capsule\.Scenes\.SceneKey RoomWideScene => new global::Capsule\.Scenes\.SceneKey\("scenes/dev/room-wide"\);""",
            generated);
        Assert.Matches(
            """\[CapsuleGeneratedSceneDocument\(Key = "scenes/room", Path = "[^"]*Assets/Scenes/room\.scene\.json"\)\]\n +\[CapsuleGeneratedPlacement\(2, "player", Line = \d+, Column = 5\)\]\n +public static global::Capsule\.Scenes\.SceneKey RoomScene => new global::Capsule\.Scenes\.SceneKey\("scenes/room"\);""",
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
            workspace.Write(document, """{"formatVersion": 7, "entities": [], "nextEntityId": 1}""");
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

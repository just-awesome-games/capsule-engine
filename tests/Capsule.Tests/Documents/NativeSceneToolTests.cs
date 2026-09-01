using Capsule.Cli;
using Capsule.Scenes.Documents;

namespace Capsule.Tests.Documents;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class NativeSceneToolTests
{
    private const string Authored = """
        { "formatVersion": 2,
          "entities": [
            { "id": 1, "type": "tile-map", "x": 0, "y": 0,
              "properties": { "tileSize": 16, "width": 2, "height": 1,
                              "tileTypes": [ { "type": "empty" }, { "type": "ground", "color": "#4a5568ff" } ],
                              "tiles": [0, 1] } },
            { "id": 2, "type": "player", "x": 8, "y": 0 } ],
          "nextEntityId": 3 }
        """;

    [Fact]
    public void ImportNative_ReEmitsCanonicallyUnderTheSourceStem()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("hall.scene.json", Authored);

        int exitCode = SceneDocumentTool.ImportNative("scenes", ["hall.scene.json"], tileSize: null, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        string emitted = File.ReadAllText("scenes/hall.scene.json");
        SceneDocument derived = SceneDocumentFile.Load("scenes/hall.scene.json");
        Assert.Equal(SceneDocumentFile.ToJson(derived), emitted);
        Assert.NotEqual(Authored, emitted);
        Assert.Equal(2, derived.Entries[0].TileMap!.Value.Grid.Width);
        Assert.Equal("player", derived.Entries[1].Entity!.Value.Type);
    }

    [Fact]
    public void ImportNative_StampsTheSourcePathItWasHanded()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("rooms/hall.scene.json", Authored);

        int exitCode = SceneDocumentTool.ImportNative(
            "scenes",
            ["rooms/hall.scene.json"],
            tileSize: null,
            TextWriter.Null,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        SceneDocument derived = SceneDocumentFile.Load("scenes/hall.scene.json");
        Assert.Equal(NativeSceneImporter.ToolName, derived.Source?.Tool);
        Assert.Equal("rooms/hall.scene.json", derived.Source?.Path);
    }

    [Fact]
    public void ImportNative_FailsAMalformedDocumentByNameAndStillImportsTheOthers()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("hall.scene.json", Authored);
        workspace.Write("broken.scene.json", """{ "formatVersion": 2, "entities": [ { "id": 1, "type": "tile-map", "x": 0, "y": 0 } ], "nextEntityId": 2 }""");

        StringWriter error = new();
        int exitCode = SceneDocumentTool.ImportNative(
            "scenes",
            ["broken.scene.json", "hall.scene.json"],
            tileSize: null,
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("broken.scene.json", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("scenes/broken.scene.json"));
        Assert.True(File.Exists("scenes/hall.scene.json"));
    }

    [Fact]
    public void ImportNative_FailsADocumentWhoseTileSizeIsNotTheDeclaredOne()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("hall.scene.json", Authored);

        StringWriter error = new();
        int exitCode = SceneDocumentTool.ImportNative("scenes", ["hall.scene.json"], tileSize: 8, TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("hall.scene.json", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("scenes/hall.scene.json"));
    }

    // The shipped plane is a valid authoring source: lifting a derived document back in and
    // re-importing it must produce the same scene, provenance aside.
    [Fact]
    public void ImportNative_RoundTripsADocumentTheTiledImporterWrote()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");
        Assert.Equal(0, SceneDocumentTool.ImportTiled("tiled", ["room.tmj"], tileSize: null, TextWriter.Null, TextWriter.Null));

        int exitCode = SceneDocumentTool.ImportNative(
            "scenes",
            ["tiled/room.scene.json"],
            tileSize: null,
            TextWriter.Null,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        SceneDocument imported = SceneDocumentFile.Load("tiled/room.scene.json");
        SceneDocument reimported = SceneDocumentFile.Load("scenes/room.scene.json");
        Assert.Equal(Unstamped(imported), Unstamped(reimported));
    }

    private static string Unstamped(SceneDocument document) =>
        SceneDocumentFile.ToJson(
            new SceneDocument(document.Entries.ToArray(), document.NextEntityId));
}

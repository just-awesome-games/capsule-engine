using Capsule.Build.Tool;
using Capsule.Scenes.Documents;

namespace Capsule.Tests.Documents;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class NativeSceneToolTests
{
    private const string Authored = """
        { "formatVersion": 4,
          "entities": [
            { "id": 1, "type": "tile-map", "x": 0, "y": 0,
              "properties": { "tileSize": 16, "width": 2, "height": 1,
                              "texture": "terrain.png", "columns": 4,
                              "tileTypes": [ { "type": "empty" }, { "type": "ground", "cell": 0 } ],
                              "tiles": [0, 1] } },
            { "id": 2, "type": "player", "x": 8, "y": 0 } ],
          "nextEntityId": 3 }
        """;

    [Fact]
    public void Import_ReEmitsCanonicallyUnderTheSourceStemIntoAnOutputDirectoryItCreates()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("hall.scene.json", Authored);
        const string Output = "obj/capsule/scenes";

        int exitCode = SceneDocumentTool.Import(Output, ["hall.scene.json"], tileSize: null, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        string emitted = File.ReadAllText(Path.Combine(Output, "hall.scene.json"));
        SceneDocument derived = SceneDocumentFile.Load(Path.Combine(Output, "hall.scene.json"));
        Assert.Equal(SceneDocumentFile.ToJson(derived), emitted);
        Assert.NotEqual(Authored, emitted);
        Assert.Equal(2, derived.Entries[0].TileMap!.Value.Grid.Width);
        Assert.Equal("player", derived.Entries[1].Entity!.Value.Type);
    }

    [Fact]
    public void ImportFromList_ImportsTheSourcesNamedOnePerLine()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("room.scene.json", Authored);
        workspace.Write("hall.scene.json", Authored);
        string list = workspace.Write("scenes.txt", "room.scene.json\n\nhall.scene.json\n");

        int exitCode = SceneDocumentTool.ImportFromList("scenes", list, tileSize: null, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists("scenes/room.scene.json"));
        Assert.True(File.Exists("scenes/hall.scene.json"));
    }

    [Fact]
    public void Import_StampsAnUnstampedDocumentWithTheSourcePathItWasHanded()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("rooms/hall.scene.json", Authored);

        int exitCode = SceneDocumentTool.Import("scenes", ["rooms/hall.scene.json"], tileSize: null, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        SceneDocument derived = SceneDocumentFile.Load("scenes/hall.scene.json");
        Assert.Equal(NativeSceneImporter.ToolName, derived.Source?.Tool);
        Assert.Equal("rooms/hall.scene.json", derived.Source?.Path);
    }

    // An authoring module's document arrives already stamped with the file a person edited, and
    // that is the provenance a shipped document must keep.
    [Fact]
    public void Import_PreservesTheSourceBlockOfADocumentAModuleDerived()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        SceneDocument stamped = new(
            SceneDocumentFile.Parse(Authored).Entries.ToArray(),
            3,
            new SceneDocumentSource("editor", "../asset-sources/scenes/hall.editor", new string('a', 64)));
        workspace.Write("obj/editor/hall.scene.json", SceneDocumentFile.ToJson(stamped));

        int exitCode = SceneDocumentTool.Import("scenes", ["obj/editor/hall.scene.json"], tileSize: null, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Equal(stamped.Source, SceneDocumentFile.Load("scenes/hall.scene.json").Source);
    }

    [Fact]
    public void Import_FailsAMalformedDocumentByNameAndStillImportsTheOthers()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("hall.scene.json", Authored);
        workspace.Write("broken.scene.json", """{ "formatVersion": 4, "entities": [ { "id": 1, "type": "tile-map", "x": 0, "y": 0 } ], "nextEntityId": 2 }""");

        StringWriter error = new();
        int exitCode = SceneDocumentTool.Import("scenes", ["broken.scene.json", "hall.scene.json"], tileSize: null, TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("broken.scene.json", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("scenes/broken.scene.json"));
        Assert.True(File.Exists("scenes/hall.scene.json"));
    }

    [Fact]
    public void Import_FailsADocumentWhoseTileSizeIsNotTheDeclaredOne()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("hall.scene.json", Authored);

        StringWriter error = new();
        int exitCode = SceneDocumentTool.Import("scenes", ["hall.scene.json"], tileSize: 8, TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("hall.scene.json", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("scenes/hall.scene.json"));
    }

    [Fact]
    public void Import_RefusesTwoSourcesThatWouldClaimTheSameDocument()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("a/room.scene.json", Authored);
        workspace.Write("b/room.scene.json", Authored);

        StringWriter error = new();
        int exitCode = SceneDocumentTool.Import("scenes", ["a/room.scene.json", "b/room.scene.json"], tileSize: null, TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("would overwrite", error.ToString(), StringComparison.Ordinal);
    }
}

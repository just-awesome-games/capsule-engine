using Capsule.Build;
using Capsule.Build.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class NativeSceneToolTests
{
    private const string Authored = SceneDocumentFixtures.AuthoredTileMapAndPlayer;

    [Fact]
    public void Import_ReEmitsCanonicallyUnderTheSourceStemIntoAnOutputDirectoryItCreates()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("hall.scene.json", Authored);
        const string Output = "obj/capsule/scenes";

        int exitCode = SceneDocumentTool.Import(Output, Sources("hall.scene.json"), tileSize: null, Fields(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        string emitted = File.ReadAllText(Path.Combine(Output, "hall.scene.json"));
        SceneDocument derived = SceneDocumentFile.Load(Path.Combine(Output, "hall.scene.json"));
        Assert.Equal(SceneDocumentFile.ToJson(derived), emitted);
        Assert.NotEqual(Authored, emitted);
        Assert.Equal(2, derived.Entries[0].TileMap!.Value.Grid.Width);
        Assert.Equal("player", derived.Entries[1].Entity!.Value.Type);
    }

    // A texture is reached by its key, so the derived document names the path the build ships it at.
    [Fact]
    public void Import_ReEmitsATileMapTextureAsItsKey()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write(
            "hall.scene.json",
            Authored.Replace("\"terrain.png\"", "\"Terrain/Cave_Wall.png\"", StringComparison.Ordinal));

        int exitCode = SceneDocumentTool.Import("scenes", Sources("hall.scene.json"), tileSize: null, Fields(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        SceneDocument derived = SceneDocumentFile.Load("scenes/hall.scene.json");
        Assert.Equal("terrain/cave-wall", derived.Entries[0].TileMap!.Value.Grid.Texture?.Name);
    }

    [Fact]
    public void Import_StampsAnUnstampedDocumentWithTheSourcePathItWasHanded()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("rooms/hall.scene.json", Authored);

        int exitCode = SceneDocumentTool.Import("scenes", Sources("rooms/hall.scene.json"), tileSize: null, Fields(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        SceneDocument derived = SceneDocumentFile.Load("scenes/hall.scene.json");
        Assert.Equal(NativeSceneImporter.ToolName, derived.Source?.Tool);
        Assert.Equal("rooms/hall.scene.json", derived.Source?.Path);
    }

    // A module's document arrives stamped with the file a person edited; that provenance is kept.
    [Fact]
    public void Import_PreservesTheSourceBlockOfADocumentAModuleDerived()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        SceneDocument stamped = new(
            SceneDocumentFile.Parse(Authored).Entries.ToArray(),
            3,
            new SceneDocumentSource("editor", "Assets/Scenes/hall.editor", new string('a', 64)));
        workspace.Write("obj/editor/hall.scene.json", SceneDocumentFile.ToJson(stamped));

        int exitCode = SceneDocumentTool.Import("scenes", Sources("obj/editor/hall.scene.json"), tileSize: null, Fields(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Equal(stamped.Source, SceneDocumentFile.Load("scenes/hall.scene.json").Source);
    }

    [Fact]
    public void Import_FailsAMalformedDocumentByNameAndStillImportsTheOthers()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("hall.scene.json", Authored);
        workspace.Write("broken.scene.json", """{ "formatVersion": 6, "entities": [ { "id": 1, "type": "tile-map", "x": 0, "y": 0 } ], "nextEntityId": 2 }""");

        StringWriter error = new();
        int exitCode = SceneDocumentTool.Import("scenes", Sources("broken.scene.json", "hall.scene.json"), tileSize: null, Fields(), TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("broken.scene.json", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("declares no properties", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("scenes/broken.scene.json"));
        Assert.True(File.Exists("scenes/hall.scene.json"));
    }

    [Fact]
    public void Import_FailsADocumentWhoseTileSizeIsNotTheDeclaredOne()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("hall.scene.json", Authored);

        StringWriter error = new();
        int exitCode = SceneDocumentTool.Import("scenes", Sources("hall.scene.json"), tileSize: 8, Fields(), TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("hall.scene.json", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("scenes/hall.scene.json"));
    }

    // A key nests, so a document filed under a directory ships under it.
    [Fact]
    public void Import_WritesANestedKeyAtItsPath()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("stage-1/room-01.scene.json", Authored);

        int exitCode = SceneDocumentTool.Import(
            "scenes",
            [new DocumentSource("stage-1/room-01", "stage-1/room-01.scene.json")],
            tileSize: null,
            Fields(),
            TextWriter.Null,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists("scenes/stage-1/room-01.scene.json"));
    }

    // The stem at the root is the key a source carries when nothing states one.
    private static DocumentSource[] Sources(params string[] paths) =>
        [.. paths.Select(static path => new DocumentSource(
            Path.GetFileName(path)[..^SceneDocumentTool.DocumentExtension.Length], path))];

    // No test here reads what Import resolved, only whether the derived document lands.
    private static Dictionary<string, (string? BaseScene, string? Camera)> Fields() => new(StringComparer.Ordinal);
}

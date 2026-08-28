using Capsule.Maps;
using Capsule.Maps.Cli;

namespace Capsule.Tests.Maps;

[Collection(MapWorkspaceCollection.Name)]
public sealed class NativeMapToolTests
{
    private const string Authored = """
        { "formatVersion": 1,
          "grid": { "tileSize": 16, "width": 2, "height": 1,
                    "tileTypes": [ { "type": "empty" }, { "type": "ground", "color": "#4a5568ff" } ],
                    "tiles": [0, 1] },
          "objects": [ { "id": 1, "type": "player", "x": 8, "y": 0 } ],
          "nextObjectId": 2 }
        """;

    [Fact]
    public void ImportNative_ReEmitsCanonicallyUnderTheSourceStem()
    {
        using MapFixtures.Workspace workspace = new();
        workspace.Write("hall.map.json", Authored);

        int exitCode = MapTool.ImportNative("maps", ["hall.map.json"], tileSize: null, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        string emitted = File.ReadAllText("maps/hall.map.json");
        Map derived = MapFile.Load("maps/hall.map.json");
        Assert.Equal(MapFile.ToJson(derived), emitted);
        Assert.NotEqual(Authored, emitted);
        Assert.Equal(2, derived.Grid.Width);
        Assert.Equal("player", derived.Objects[0].Type);
    }

    [Fact]
    public void ImportNative_StampsTheSourcePathItWasHanded()
    {
        using MapFixtures.Workspace workspace = new();
        workspace.Write("rooms/hall.map.json", Authored);

        int exitCode = MapTool.ImportNative(
            "maps",
            ["rooms/hall.map.json"],
            tileSize: null,
            TextWriter.Null,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Map derived = MapFile.Load("maps/hall.map.json");
        Assert.Equal(NativeMapImporter.ToolName, derived.Source?.Tool);
        Assert.Equal("rooms/hall.map.json", derived.Source?.Path);
    }

    [Fact]
    public void ImportNative_FailsAMalformedMapByNameAndStillImportsTheOthers()
    {
        using MapFixtures.Workspace workspace = new();
        workspace.Write("hall.map.json", Authored);
        workspace.Write("broken.map.json", """{ "formatVersion": 1, "grid": null }""");

        StringWriter error = new();
        int exitCode = MapTool.ImportNative(
            "maps",
            ["broken.map.json", "hall.map.json"],
            tileSize: null,
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("broken.map.json", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("maps/broken.map.json"));
        Assert.True(File.Exists("maps/hall.map.json"));
    }

    [Fact]
    public void ImportNative_FailsAMapWhoseTileSizeIsNotTheDeclaredOne()
    {
        using MapFixtures.Workspace workspace = new();
        workspace.Write("hall.map.json", Authored);

        StringWriter error = new();
        int exitCode = MapTool.ImportNative("maps", ["hall.map.json"], tileSize: 8, TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("hall.map.json", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("maps/hall.map.json"));
    }

    [Fact]
    public void ImportNative_RefusesTwoSourcesThatWouldClaimTheSameMap()
    {
        using MapFixtures.Workspace workspace = new();
        workspace.Write("a/hall.map.json", Authored);
        workspace.Write("b/hall.map.json", Authored);

        StringWriter error = new();
        int exitCode = MapTool.ImportNative(
            "maps",
            ["a/hall.map.json", "b/hall.map.json"],
            tileSize: null,
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("would overwrite", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ImportNative_RoundTripsAMapTheTiledImporterWrote()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("room");
        Assert.Equal(0, MapTool.ImportTiled("tiled", ["room.tmj"], tileSize: null, TextWriter.Null, TextWriter.Null));

        int exitCode = MapTool.ImportNative(
            "maps",
            ["tiled/room.map.json"],
            tileSize: null,
            TextWriter.Null,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Map imported = MapFile.Load("tiled/room.map.json");
        Map reimported = MapFile.Load("maps/room.map.json");
        Assert.Equal(MapFile.ToJson(new Map(imported.Grid, imported.Objects.ToArray(), imported.NextObjectId)),
                     MapFile.ToJson(new Map(reimported.Grid, reimported.Objects.ToArray(), reimported.NextObjectId)));
    }
}

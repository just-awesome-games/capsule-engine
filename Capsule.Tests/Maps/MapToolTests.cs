using Capsule.Maps;
using Capsule.Maps.Cli;

namespace Capsule.Tests.Maps;

/// <summary>
/// The tool as the build hook drives it: one invocation, the whole stale batch, each map named
/// after its source.
/// </summary>
[Collection(MapWorkspaceCollection.Name)]
public sealed class MapToolTests
{
    // The build hook's geometry: an output directory that does not exist yet, several maps per
    // run, and a stamp that names the source the build passed rather than anything about where
    // the map landed — obj/ is a build detail and no provenance should mention it.
    [Fact]
    public void ImportTiled_WritesOneMapPerSourceIntoAnOutputDirectoryItCreates()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("room", "hall");
        const string Output = "obj/capsule/maps";

        int exitCode = MapTool.ImportTiled(
            Output,
            ["room.tmj", "hall.tmj"],
            tileSize: null,
            TextWriter.Null,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(Output, "hall.map.json")));
        Assert.Equal(
            "room.tmj",
            MapFile.Load(Path.Combine(Output, "room.map.json")).Source?.Path);
    }

    // The form every build actually takes, because a project's worth of source paths does not
    // fit on a command line.
    [Fact]
    public void ImportTiledFromList_ImportsTheSourcesNamedOnePerLine()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("room", "hall");
        string list = workspace.Write("maps.txt", "room.tmj\n\nhall.tmj\n");

        int exitCode = MapTool.ImportTiledFromList("maps", list, tileSize: null, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists("maps/room.map.json"));
        Assert.True(File.Exists("maps/hall.map.json"));
    }

    // A batch is one process for the whole build, so a single unimportable source must not cost
    // the rest of the maps.
    [Fact]
    public void ImportTiled_ReportsAFailedSourceByNameAndStillImportsTheOthers()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("room");
        workspace.Write("broken.tmj", "{ not tiled json");

        StringWriter error = new();
        int exitCode = MapTool.ImportTiled(
            "maps",
            ["broken.tmj", "room.tmj"],
            tileSize: null,
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("broken.tmj", error.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists("maps/room.map.json"));
    }

    // The declared tile size reaches the importer through the same argument the build hook
    // fills from CapsuleTileSize, and a map that breaks it fails the build like any other.
    [Fact]
    public void ImportTiled_FailsAMapWhoseTileSizeIsNotTheDeclaredOne()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("room");

        StringWriter error = new();
        int exitCode = MapTool.ImportTiled("maps", ["room.tmj"], tileSize: 8, TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("room.tmj", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("maps/room.map.json"));
    }

    // Maps are named after their sources and the output tree is flat, so two sources of the
    // same name would otherwise leave one silently overwritten by the other.
    [Fact]
    public void ImportTiled_RefusesTwoSourcesThatWouldClaimTheSameMap()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("a/room", "b/room");

        StringWriter error = new();
        int exitCode = MapTool.ImportTiled(
            "maps",
            ["a/room.tmj", "b/room.tmj"],
            tileSize: null,
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("would overwrite", error.ToString(), StringComparison.Ordinal);
    }
}

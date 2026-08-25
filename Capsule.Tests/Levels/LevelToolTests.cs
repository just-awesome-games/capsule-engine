using Capsule.Levels;
using Capsule.Levels.Cli;

namespace Capsule.Tests.Levels;

/// <summary>
/// The tool as the build hook drives it: one invocation, the whole stale batch, each level
/// named after its map.
/// </summary>
[Collection(LevelWorkspaceCollection.Name)]
public sealed class LevelToolTests
{
    // The build hook's geometry: an output directory that does not exist yet, several levels
    // per run, and a stamp that names the map the build passed rather than anything about where
    // the level landed — obj/ is a build detail and no provenance should mention it.
    [Fact]
    public void ImportTiled_WritesOneLevelPerMapIntoAnOutputDirectoryItCreates()
    {
        using LevelFixtures.Workspace workspace = LevelFixtures.CopyMaps("room", "hall");
        const string Output = "obj/capsule/levels";

        int exitCode = LevelTool.ImportTiled(
            Output,
            ["room.tmj", "hall.tmj"],
            TextWriter.Null,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(Output, "hall.level.json")));
        Assert.Equal(
            "room.tmj",
            LevelFile.Load(Path.Combine(Output, "room.level.json")).Source?.Path);
    }

    // The form every build actually takes, because a project's worth of map paths does not fit
    // on a command line.
    [Fact]
    public void ImportTiledFromList_ImportsTheMapsNamedOnePerLine()
    {
        using LevelFixtures.Workspace workspace = LevelFixtures.CopyMaps("room", "hall");
        string list = workspace.Write("maps.txt", "room.tmj\n\nhall.tmj\n");

        int exitCode = LevelTool.ImportTiledFromList("levels", list, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists("levels/room.level.json"));
        Assert.True(File.Exists("levels/hall.level.json"));
    }

    // A batch is one process for the whole build, so a single unimportable map must not cost
    // the rest of the levels.
    [Fact]
    public void ImportTiled_ReportsAFailedMapByNameAndStillImportsTheOthers()
    {
        using LevelFixtures.Workspace workspace = LevelFixtures.CopyMaps("room");
        workspace.Write("broken.tmj", "{ not tiled json");

        StringWriter error = new();
        int exitCode = LevelTool.ImportTiled(
            "levels",
            ["broken.tmj", "room.tmj"],
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("broken.tmj", error.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists("levels/room.level.json"));
    }

    // Levels are named after their maps and the output tree is flat, so two maps of the same
    // name would otherwise leave one silently overwritten by the other.
    [Fact]
    public void ImportTiled_RefusesTwoMapsThatWouldClaimTheSameLevel()
    {
        using LevelFixtures.Workspace workspace = LevelFixtures.CopyMaps("a/room", "b/room");

        StringWriter error = new();
        int exitCode = LevelTool.ImportTiled(
            "levels",
            ["a/room.tmj", "b/room.tmj"],
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("would overwrite", error.ToString(), StringComparison.Ordinal);
    }
}

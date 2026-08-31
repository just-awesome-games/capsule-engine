using Capsule.Cli;
using Capsule.Scenes.Documents;

namespace Capsule.Tests.Documents;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class SceneDocumentToolTests
{
    [Fact]
    public void ImportTiled_WritesOneDocumentPerSourceIntoAnOutputDirectoryItCreates()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room", "hall");
        const string Output = "obj/capsule/scenes";

        int exitCode = SceneDocumentTool.ImportTiled(
            Output,
            ["room.tmj", "hall.tmj"],
            tileSize: null,
            TextWriter.Null,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(Output, "hall.scene.json")));
        Assert.Equal(
            "room.tmj",
            SceneDocumentFile.Load(Path.Combine(Output, "room.scene.json")).Source?.Path);
    }

    [Fact]
    public void ImportTiledFromList_ImportsTheSourcesNamedOnePerLine()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room", "hall");
        string list = workspace.Write("scenes.txt", "room.tmj\n\nhall.tmj\n");

        int exitCode = SceneDocumentTool.ImportTiledFromList("scenes", list, tileSize: null, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists("scenes/room.scene.json"));
        Assert.True(File.Exists("scenes/hall.scene.json"));
    }

    [Fact]
    public void ImportTiled_ReportsAFailedSourceByNameAndStillImportsTheOthers()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");
        workspace.Write("broken.tmj", "{ not tiled json");

        StringWriter error = new();
        int exitCode = SceneDocumentTool.ImportTiled(
            "scenes",
            ["broken.tmj", "room.tmj"],
            tileSize: null,
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("broken.tmj", error.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists("scenes/room.scene.json"));
    }

    [Fact]
    public void ImportTiled_FailsASourceWhoseTileSizeIsNotTheDeclaredOne()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        StringWriter error = new();
        int exitCode = SceneDocumentTool.ImportTiled("scenes", ["room.tmj"], tileSize: 8, TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("room.tmj", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("scenes/room.scene.json"));
    }

    [Fact]
    public void ImportTiled_RefusesTwoSourcesThatWouldClaimTheSameDocument()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("a/room", "b/room");

        StringWriter error = new();
        int exitCode = SceneDocumentTool.ImportTiled(
            "scenes",
            ["a/room.tmj", "b/room.tmj"],
            tileSize: null,
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("would overwrite", error.ToString(), StringComparison.Ordinal);
    }
}

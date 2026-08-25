using Capsule.Levels;
using Capsule.Levels.Cli;

namespace Capsule.Tests.Levels;

/// <summary>
/// The CLI verbs. <c>validate</c> is the commit gate — it is what makes a generated level
/// file safe to keep in a repository beside its source.
/// </summary>
public sealed class LevelToolTests
{
    private const string HandAuthored = """
        {"tileSize":16,"width":2,"height":1,"tileTypes":["empty","ground"],"tiles":[0,1],
         "entities":[{"type":"coin","x":8,"y":0},{"id":1,"type":"player","x":0,"y":0},{"type":"gem","x":16,"y":0}],
         "nextEntityId":2}
        """;

    [Fact]
    public void AssignIds_NumbersUnnumberedEntitiesInFileOrderAndAdvancesTheCounter()
    {
        using LevelFixtures.Workspace workspace = new();
        string path = workspace.Write("hand.level.json", HandAuthored);

        Assert.Equal(0, LevelTool.AssignIds(path, TextWriter.Null, TextWriter.Null));

        Level level = LevelFile.Load(path);
        int[] ids = [.. level.Entities.ToArray().Select(entity => entity.Id)];
        Assert.Equal(new[] { 2, 1, 3 }, ids);
        Assert.Equal(4, level.NextEntityId);
    }

    [Fact]
    public void AssignIds_RewritesTheFileCanonically()
    {
        using LevelFixtures.Workspace workspace = new();
        string path = workspace.Write("hand.level.json", HandAuthored);

        LevelTool.AssignIds(path, TextWriter.Null, TextWriter.Null);

        Assert.StartsWith("{\n  \"tileSize\": 16,\n", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsAGeneratedLevelBesideItsSource()
    {
        using LevelFixtures.Workspace workspace = LevelFixtures.CopyRoom();

        Assert.Equal(0, LevelTool.Validate([workspace.Path("room.level.json")], TextWriter.Null, TextWriter.Null));
    }

    [Fact]
    public void Validate_AcceptsAHandAuthoredLevelWithNoSource()
    {
        using LevelFixtures.Workspace workspace = new();
        string path = workspace.Write("hand.level.json", HandAuthored);
        LevelTool.AssignIds(path, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, LevelTool.Validate([path], TextWriter.Null, TextWriter.Null));
    }

    // The anti-footgun gate. A hand-edit to a generated file is invisible in review and
    // silently reverted by the next import, so it has to fail before the commit.
    [Fact]
    public void Validate_RejectsAHandEditOfAGeneratedLevelAndNamesTheSource()
    {
        using LevelFixtures.Workspace workspace = LevelFixtures.CopyRoom();
        string path = workspace.Path("room.level.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"coin\"", "\"gem\"", StringComparison.Ordinal));

        StringWriter error = new();
        Assert.Equal(1, LevelTool.Validate([path], TextWriter.Null, error));

        Assert.Contains("does not match its source", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("room.tmj", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAGeneratedLevelWhoseSourceIsGone()
    {
        using LevelFixtures.Workspace workspace = LevelFixtures.CopyRoom();
        File.Delete(workspace.Path("room.tmj"));

        StringWriter error = new();
        Assert.Equal(1, LevelTool.Validate([workspace.Path("room.level.json")], TextWriter.Null, error));

        Assert.Contains("is missing", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ImportTiled_ReportsAFailureRatherThanThrowing()
    {
        using LevelFixtures.Workspace workspace = new();

        StringWriter error = new();
        int exitCode = LevelTool.ImportTiled(
            workspace.Path("absent.tmj"),
            workspace.Path("out.level.json"),
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.NotEmpty(error.ToString());
    }
}

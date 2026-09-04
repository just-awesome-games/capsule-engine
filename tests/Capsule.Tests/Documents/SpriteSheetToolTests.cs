using Capsule.Build;
using Capsule.Build.Sprites;

namespace Capsule.Tests.Documents;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class SpriteSheetToolTests
{
    private const string Authored = """
        { "formatVersion": 1,
          "texture": "player.png",
          "frames": [
            { "name": "idle-0", "x": 0, "y": 0, "width": 8, "height": 8, "pivot": [4, 8] },
            { "name": "run-0", "x": 8, "y": 0, "width": 8, "height": 8, "pivot": [4, 8] } ],
          "clips": [
            { "name": "run", "loop": true, "frames": [ { "frame": "run-0", "ticks": 4 } ] } ] }
        """;

    [Fact]
    public void ImportReEmitsCanonicallyAndRendersTheWholeSetAsOneGeneratedFile()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("player.sheet.json", Authored);

        int exitCode = SpriteSheetTool.Import(
            "obj/sprites", ["player.sheet.json"], ["player.png"], "obj/GameSprites.g.cs", TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        string emitted = File.ReadAllText("obj/sprites/player.sheet.json");
        Assert.Equal(SpriteSheetDocumentFile.ToJson(SpriteSheetDocumentFile.Load("obj/sprites/player.sheet.json")), emitted);

        string generated = File.ReadAllText("obj/GameSprites.g.cs");
        Assert.Contains("public static class GameSprites", generated, StringComparison.Ordinal);
        Assert.Contains("public static class Player", generated, StringComparison.Ordinal);
        Assert.Contains("Sprite Idle0 =>", generated, StringComparison.Ordinal);
        Assert.Contains("SpriteClip Run { get; }", generated, StringComparison.Ordinal);
        Assert.Contains("new int[] { 4 }", generated, StringComparison.Ordinal);
    }

    // The order the build collected the sources in must not reach the generated file, or the same
    // sheets produce a different diff on another machine.
    [Fact]
    public void SheetsAreRenderedInNameOrderWhateverOrderTheyArrivedIn()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("player.sheet.json", Authored);
        workspace.Write("boss.sheet.json", Authored);

        int forwards = SpriteSheetTool.Import(
            "obj/a", ["player.sheet.json", "boss.sheet.json"], ["player.png"], "obj/a.g.cs", TextWriter.Null, TextWriter.Null);
        int backwards = SpriteSheetTool.Import(
            "obj/b", ["boss.sheet.json", "player.sheet.json"], ["player.png"], "obj/b.g.cs", TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, forwards);
        Assert.Equal(0, backwards);
        Assert.Equal(File.ReadAllText("obj/a.g.cs"), File.ReadAllText("obj/b.g.cs"));
    }

    [Fact]
    public void ASheetCuttingFromATextureTheGameDoesNotShipFails()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("player.sheet.json", Authored);
        StringWriter error = new();

        int exitCode = SpriteSheetTool.Import(
            "obj/sprites", ["player.sheet.json"], ["tiles.png"], "obj/GameSprites.g.cs", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("player.sheet.json: cuts from texture \"player.png\"", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("obj/GameSprites.g.cs"));
    }

    // The runtime's texture store is keyed by the shipped spelling, so a case-blind match here
    // would emit a Sprite carrying a handle nothing ever loaded — a black frame at run time.
    [Fact]
    public void ASheetCuttingFromATextureThatDiffersOnlyInCaseFails()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("player.sheet.json", Authored.Replace("player.png", "player.PNG", StringComparison.Ordinal));
        StringWriter error = new();

        int exitCode = SpriteSheetTool.Import(
            "obj/sprites", ["player.sheet.json"], ["player.png"], "obj/GameSprites.g.cs", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("player.sheet.json: cuts from texture \"player.PNG\"", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists("obj/GameSprites.g.cs"));
    }

    [Fact]
    public void TwoSourcesSharingAStemFail()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("player.sheet.json", Authored);
        workspace.Write("nested/player.sheet.json", Authored);
        StringWriter error = new();

        int exitCode = SpriteSheetTool.Import(
            "obj/sprites",
            ["player.sheet.json", "nested/player.sheet.json"],
            ["player.png"],
            "obj/GameSprites.g.cs",
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("would overwrite", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ASheetWhoseNameIsNoCSharpNameFails()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("2-player.sheet.json", Authored);
        StringWriter error = new();

        int exitCode = SpriteSheetTool.Import(
            "obj/sprites", ["2-player.sheet.json"], ["player.png"], "obj/GameSprites.g.cs", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("no C# name", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ImportFromListReadsTheSourcesAndTexturesOnePerLine()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("player.sheet.json", Authored);
        string sheets = workspace.Write("sheets.txt", "player.sheet.json\n\n");
        string textures = workspace.Write("textures.txt", "player.png\ntiles.png\n");

        int exitCode = SpriteSheetTool.ImportFromList(
            "obj/sprites", sheets, textures, "obj/GameSprites.g.cs", TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists("obj/GameSprites.g.cs"));
    }
}

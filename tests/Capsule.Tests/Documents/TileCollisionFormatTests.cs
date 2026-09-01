using Capsule.Cli.Tiled;
using Capsule.Collision;
using Capsule.Rendering;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Documents;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TileCollisionFormatTests
{
    [Theory]
    [InlineData(TileCollision.Solid, "box")]
    [InlineData(TileCollision.OneWay, "one-way")]
    public void ATileTypesCollision_SurvivesItsOwnRoundTrip(TileCollision collision, string spelling)
    {
        string written = SceneDocumentFile.ToJson(Document(collision));

        Assert.Contains($"\"collision\": \"{spelling}\"", written, StringComparison.Ordinal);
        Assert.Equal(collision, Palette(SceneDocumentFile.Parse(written))[1].Collision);
        Assert.Equal(written, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(written)));
    }

    [Fact]
    public void ATileTypeThatCollidesWithNothing_WritesNoCollisionField()
    {
        string written = SceneDocumentFile.ToJson(Document(TileCollision.None));

        Assert.DoesNotContain("collision", written, StringComparison.Ordinal);
        Assert.Equal(TileCollision.None, Palette(SceneDocumentFile.Parse(written))[1].Collision);
    }

    [Fact]
    public void AnUnknownCollisionSpelling_FailsTheDocument()
    {
        string written = SceneDocumentFile.ToJson(Document(TileCollision.Solid))
            .Replace("\"box\"", "\"slope\"", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("tileTypes[1].collision is \"slope\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("box, one-way", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReservedEmptyEntry_MayNotCollide()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            2,
            1,
            [new TileDefinition("empty", null, TileCollision.Solid), Ground(TileCollision.Solid)],
            [0, 1]));

        Assert.Contains("no colour and no collision", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("box", TileCollision.Solid)]
    [InlineData("one-way", TileCollision.OneWay)]
    public void Import_ReadsATilesCollisionProperty(string spelling, TileCollision collision)
    {
        SceneDocument document = ImportWithTileProperty(
            $"{{\"name\":\"collision\",\"type\":\"string\",\"value\":\"{spelling}\"}},");

        Assert.Equal(collision, Palette(document)[1].Collision);
        Assert.Equal(TileCollision.None, Palette(document)[2].Collision);
    }

    [Fact]
    public void Import_LeavesATileWithNoCollisionPropertyCollidingWithNothing()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj");

        Assert.All(
            Palette(document).ToArray(),
            definition => Assert.Equal(TileCollision.None, definition.Collision));
    }

    [Fact]
    public void Import_RejectsACollisionPropertyThatSpellsSomethingElse()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"collision\",\"type\":\"string\",\"value\":\"slope\"},"));

        Assert.Contains("'collision' = 'slope'", error.Message, StringComparison.Ordinal);
        Assert.Contains("box or one-way", error.Message, StringComparison.Ordinal);
    }

    // Several of Tiled's property types carry a string value, and a well-spelled one of the wrong
    // type would otherwise import as a real collision kind.
    [Fact]
    public void Import_RejectsACollisionPropertyNotDeclaredAsAString()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"collision\",\"type\":\"file\",\"value\":\"box\"},"));

        Assert.Contains("tileset 'terrain' tile 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("Class 'ground'", error.Message, StringComparison.Ordinal);
        Assert.Contains("as a 'file' property", error.Message, StringComparison.Ordinal);
    }

    private static SceneDocument ImportWithTileProperty(string property)
    {
        // Injected into the first tile's property list, ahead of its colour.
        string tileset = SceneDocumentFixtures.Read("tiles.tsj").Replace(
            "\"properties\":[\n                {\n                 \"name\":\"color\",\n                 \"type\":\"color\",\n                 \"value\":\"#ff4a5568\"\n                }]",
            $"\"properties\":[{property}\n                {{\n                 \"name\":\"color\",\n                 \"type\":\"color\",\n                 \"value\":\"#ff4a5568\"\n                }}]",
            StringComparison.Ordinal);

        Assert.NotEqual(SceneDocumentFixtures.Read("tiles.tsj"), tileset);

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);

        return TiledImporter.Import(workspace.Write("room.tmj", SceneDocumentFixtures.Read("room.tmj")));
    }

    private static ReadOnlySpan<TileDefinition> Palette(SceneDocument document) =>
        document.Entries[0].TileMap!.Value.Grid.TileTypes;

    private static TileDefinition Ground(TileCollision collision) =>
        new("ground", new ColorRgba(0x4A, 0x55, 0x68), collision);

    private static SceneDocument Document(TileCollision collision) =>
        new(
            [
                new TileMapPlacement(
                    1,
                    new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Ground(collision)], [0, 1])),
            ],
            2);
}

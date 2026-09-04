using Capsule.Assets;
using Capsule.Collision;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Documents;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TileLayerFormatTests
{
    private static readonly TextureHandle Atlas = new("terrain", ".png");

    [Fact]
    public void ATileTypesLayerAndCollidableFaces_SurviveTheirOwnRoundTrip()
    {
        string written = SceneDocumentFile.ToJson(Document("platform", CellFaces2D.Top));

        Assert.Contains("\"layer\": \"platform\"", written, StringComparison.Ordinal);
        Assert.Contains("\"top\"", written, StringComparison.Ordinal);

        TileDefinition read = Palette(SceneDocumentFile.Parse(written))[1];
        Assert.Equal("platform", read.Layer);
        Assert.Equal(CellFaces2D.Top, read.CollidableFaces);
        Assert.Equal(written, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(written)));
    }

    [Fact]
    public void ATileTypeThatCollidesWithNothing_WritesNeitherLayerNorCollidableFaces()
    {
        string written = SceneDocumentFile.ToJson(Document(null, CellFaces2D.All));

        Assert.DoesNotContain("layer", written, StringComparison.Ordinal);
        Assert.DoesNotContain("collidableFaces", written, StringComparison.Ordinal);
        Assert.Null(Palette(SceneDocumentFile.Parse(written))[1].Layer);
    }

    // Every side is the default, so a tile that collides as its whole box says nothing about faces.
    [Fact]
    public void ATileTypeCollidingOnEverySide_WritesNoCollidableFaces()
    {
        string written = SceneDocumentFile.ToJson(Document("solid", CellFaces2D.All));

        Assert.DoesNotContain("collidableFaces", written, StringComparison.Ordinal);
        Assert.Equal(CellFaces2D.All, Palette(SceneDocumentFile.Parse(written))[1].CollidableFaces);
    }

    [Fact]
    public void AVersionOneDocument_IsRefused()
    {
        string written = SceneDocumentFile.ToJson(Document("solid", CellFaces2D.All))
            .Replace("\"formatVersion\": 4", "\"formatVersion\": 1", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("formatVersion 1 is unsupported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownFaceSpelling_FailsTheDocument()
    {
        string written = SceneDocumentFile.ToJson(Document("platform", CellFaces2D.Top))
            .Replace("\"top\"", "\"sideways\"", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("tileTypes[1].collidableFaces holds \"sideways\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("left, right, top, bottom", error.Message, StringComparison.Ordinal);
    }

    // Faces on a tile that collides as nothing describe sides of something that is never there.
    [Fact]
    public void CollidableFacesOnATileWithNoLayer_FailTheDocument()
    {
        string written = SceneDocumentFile.ToJson(Document("platform", CellFaces2D.Top))
            .Replace("\"layer\": \"platform\",\n", string.Empty, StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("collides as nothing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReservedEmptyEntry_MayNotCollide()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            2,
            1,
            [new TileDefinition("empty", null, "solid"), Ground("solid", CellFaces2D.All)],
            [0, 1],
            Atlas,
            4));

        Assert.Contains("no cell and no layer", error.Message, StringComparison.Ordinal);
    }

    // A tile on a layer with no face collides with nothing, which decoration already spells.
    [Fact]
    public void ATileOnALayerWithNoCollidableFaces_IsRefusedByTheGrid()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            2,
            1,
            [TileGrid.EmptyTile, Ground("solid", CellFaces2D.None)],
            [0, 1],
            Atlas,
            4));

        Assert.Contains("no collidableFaces", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATileWithCollidableFacesButNoLayer_IsRefusedByTheGrid()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            2,
            1,
            [TileGrid.EmptyTile, Ground(null, CellFaces2D.Top)],
            [0, 1],
            Atlas,
            4));

        Assert.Contains("collidableFaces but no layer", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("{ \"shape\": \"box\" }")]
    [InlineData("[\"box\"]")]
    [InlineData("\"box\"")]
    public void ACollisionFieldOfAnyValue_IsRefusedWithThePointerToItsReplacements(string value)
    {
        string written = SceneDocumentFile.ToJson(Document("solid", CellFaces2D.All))
            .Replace("\"layer\": \"solid\"", $"\"collision\": {value}", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("tileTypes[1] declares collision", error.Message, StringComparison.Ordinal);
        Assert.Contains("collidableFaces", error.Message, StringComparison.Ordinal);
    }

    // Several property types carry a string value; one of the wrong type is not a layer name.
    private static ReadOnlySpan<TileDefinition> Palette(SceneDocument document) =>
        document.Entries[0].TileMap!.Value.Grid.TileTypes;

    private static TileDefinition Ground(string? layer, CellFaces2D collidableFaces) =>
        new("ground", 0, layer, collidableFaces);

    private static SceneDocument Document(string? layer, CellFaces2D collidableFaces) =>
        new(
            [
                new TileMapPlacement(
                    1,
                    new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Ground(layer, collidableFaces)], [0, 1], Atlas, 4)),
            ],
            2);
}

using Capsule.Assets;
using Capsule.Physics;
using Capsule.Scenes.Documents;
using Capsule.Tests.Physics;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Documents;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TileLayerFormatTests
{
    private static readonly TextureHandle Atlas = SceneFixtures.TerrainAtlas;

    [Fact]
    public void ATileTypesLayerShapeAndOneWay_SurviveTheirOwnRoundTrip()
    {
        string written = SceneDocumentFile.ToJson(Document("platform", CollisionFixtures.SlopeUp, oneWay: true));

        Assert.Contains("\"layer\": \"platform\"", written, StringComparison.Ordinal);
        Assert.Contains("\"shape\": [[0, 16], [16, 0], [16, 16]]", written, StringComparison.Ordinal);
        Assert.Contains("\"oneWay\": true", written, StringComparison.Ordinal);

        TileDefinition read = Palette(SceneDocumentFile.Parse(written))[1];
        Assert.Equal("platform", read.Layer);
        Assert.Equal(CollisionFixtures.SlopeUp, read.Shape);
        Assert.True(read.OneWay);
        Assert.Equal(written, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(written)));
    }

    // The whole tile, blocking from every side, is the default and says nothing beyond its layer.
    [Theory]
    [InlineData(null)]
    [InlineData("solid")]
    public void ATileTypeWithNoShapeOrOneWay_WritesNeither(string? layer)
    {
        string written = SceneDocumentFile.ToJson(Document(layer));

        Assert.Equal(layer is not null, written.Contains("layer", StringComparison.Ordinal));
        Assert.DoesNotContain("shape", written, StringComparison.Ordinal);
        Assert.DoesNotContain("oneWay", written, StringComparison.Ordinal);
        Assert.Null(Palette(SceneDocumentFile.Parse(written))[1].Shape);
    }

    [Fact]
    public void AVersionOneDocument_IsRefused()
    {
        string written = SceneDocumentFile.ToJson(Document("solid"))
            .Replace("\"formatVersion\": 6", "\"formatVersion\": 1", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("formatVersion 1", error.Message, StringComparison.Ordinal);
    }

    // A tile's shape is a convex polygon inside its own tile, and only a colliding tile has one.
    [Theory]
    [InlineData("\"layer\": \"solid\", \"shape\": [[0, 0], [16, 0], [4, 4], [0, 16]]", "not a convex polygon")]
    [InlineData("\"layer\": \"solid\", \"shape\": [[0, 0], [20, 0], [0, 16]]", "outside its tile")]
    [InlineData("\"shape\": [[0, 16], [16, 0], [16, 16]]", "no layer")]
    [InlineData("\"oneWay\": true", "no layer")]
    [InlineData("\"layer\": \"solid\", \"solidSides\": true", "no oneWay")]
    public void ABadShapeOrOneWay_FailsTheDocument(string fields, string defect)
    {
        string written = SceneDocumentFile.ToJson(Document("solid"))
            .Replace("\"layer\": \"solid\"", fields, StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("tileTypes[1]", error.Message, StringComparison.Ordinal);
        Assert.Contains(defect, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReservedEmptyEntry_MayNotCollide()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            2,
            1,
            [new TileDefinition("empty", null, "solid"), SceneFixtures.Tile("ground", 0, "solid")],
            [0, 1],
            Atlas,
            4));

        Assert.Contains("no cell and no layer", error.Message, StringComparison.Ordinal);
    }

    private static ReadOnlySpan<TileDefinition> Palette(SceneDocument document) =>
        document.Entries[0].TileMap!.Value.Grid.TileTypes;

    private static SceneDocument Document(string? layer, Shape2D? shape = null, bool oneWay = false) =>
        new(
            [
                new TileMapPlacement(
                    1,
                    new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("ground", 0, layer, shape, oneWay)], [0, 1], Atlas, 4)),
            ],
            2);
}

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
    public void ATileTypesLayerShapeOneWayAndFrames_SurviveTheirOwnRoundTrip()
    {
        string written = SceneDocumentFile.ToJson(Document("platform", CollisionFixtures.SlopeUp, oneWay: true, frames: [new(0, 8), new(1, 4)]));

        Assert.Contains("\"layer\": \"platform\"", written, StringComparison.Ordinal);
        Assert.Contains("\"shape\": [[0, 16], [16, 0], [16, 16]]", written, StringComparison.Ordinal);
        Assert.Contains("\"oneWay\": true", written, StringComparison.Ordinal);

        TileType read = Palette(SceneDocumentFile.Parse(written))[1];
        Assert.Equal("ground", read.Name);
        Assert.Equal("platform", read.Layer);
        Assert.Equal(CollisionFixtures.SlopeUp, read.Shape);
        Assert.True(read.OneWay);
        Assert.Null(read.Cell);
        Assert.Equal([new(0, 8), new(1, 4)], read.Frames!);
        Assert.Equal(written, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(written)));
    }

    // A palette entry's class key and properties are carried for composition, in the written and the shipped
    // form. Keys named like grid fields stay inside the entry.
    [Fact]
    public void APaletteEntrysTypeAndProperties_SurviveTheWrittenAndCompactRoundTrips()
    {
        const string Type = """
            "name": "ground",
                        "type": "ice",
            """;
        const string Properties = """
            "layer": "solid",
                        "properties": {
                          "grip": 0.5,
                          "tiles": [1, 2],
                          "shape": [
                            "a b"
                          ]
                        }
            """;
        string authored = SceneDocumentFile.ToJson(Document("solid"))
            .Replace("\"name\": \"ground\",", Type.ReplaceLineEndings("\n"), StringComparison.Ordinal)
            .Replace("\"layer\": \"solid\"", Properties.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        SceneDocument document = SceneDocumentFile.Parse(authored);

        AuthoredTileType read = Grid(document).Authored![1];
        Assert.Equal("ice", read.Type);
        Assert.Equal(0.5, read.Properties!.Value.GetProperty("grip").GetDouble());
        Assert.Equal(authored, SceneDocumentFile.ToJson(document));
        Assert.Equal(authored, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(SceneDocumentFile.ToJson(document, compact: true))));
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

    // Transforms are written one grid row per line like tiles, and only when a tile is mirrored or turned.
    [Fact]
    public void Transforms_SurviveARoundTrip_AndAreAbsentWhenEveryTileIsAsAuthored()
    {
        TileGrid grid = new(
            16,
            2,
            2,
            [TileGrid.EmptyTile, SceneFixtures.Tile("ground", 0)],
            [1, 1, 0, 1],
            Atlas,
            4,
            [TileTransform.FlipX, TileTransform.None, TileTransform.None, TileTransform.Rotate90]);
        string written = SceneDocumentFile.ToJson(new SceneDocument([new TileMapPlacement(1, grid)], 2));

        Assert.Contains("\"transforms\": [\n          1, 0,\n          0, 5\n        ]", written, StringComparison.Ordinal);
        Assert.Equal(grid.Transforms.ToArray(), Grid(SceneDocumentFile.Parse(written)).Transforms.ToArray());
        Assert.Equal(written, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(written)));

        string untransformed = SceneDocumentFile.ToJson(Document("solid"));
        Assert.DoesNotContain("transforms", untransformed, StringComparison.Ordinal);
        Assert.Equal(new TileTransform[2], Grid(SceneDocumentFile.Parse(untransformed)).Transforms.ToArray());
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

    private static ReadOnlySpan<TileType> Palette(SceneDocument document) => Grid(document).TileTypes;

    private static TileGrid Grid(SceneDocument document) => document.Entries[0].TileMap!.Value.Grid;

    private static SceneDocument Document(string? layer, Shape2D? shape = null, bool oneWay = false, TileFrame[]? frames = null) =>
        new(
            [
                new TileMapPlacement(
                    1,
                    new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileType { Name = "ground", Cell = frames is null ? 0 : null, Frames = frames, Layer = layer, Shape = shape, OneWay = oneWay }], [0, 1], Atlas, 4)),
            ],
            2);
}

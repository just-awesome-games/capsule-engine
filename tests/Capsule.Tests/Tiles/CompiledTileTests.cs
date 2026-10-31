using System.Globalization;
using System.Text;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Tiles;

// The build compiles a tile map's dense tiles and transforms into packed blocks, and a map decodes them straight
// into its own blocks.
public sealed class CompiledTileTests
{
    private static readonly TileType[] Palette = TileStorageTests.Palette;

    // A map with partial blocks on two sides, an all-empty map, and one smaller than a block, decoded by the bulk
    // copy a little-endian machine takes and by the per-cell read any other takes. In the 37 by 21 map, block (0, 0)
    // holds a turned cell and block (1, 0) holds painted cells that are not turned.
    [Theory]
    [InlineData(37, 21, true, true)]
    [InlineData(37, 21, true, false)]
    [InlineData(40, 20, false, true)]
    [InlineData(5, 3, true, false)]
    public void ACompiledMap_DecodesToItsAuthoredCells(int width, int height, bool painted, bool bulk)
    {
        (int[] tiles, int[] transforms) = painted ? TileStorageTests.Scattered(width, height) : (new int[width * height], new int[width * height]);
        if (painted && width > 16)
        {
            transforms[0] = (int)TileTransform.FlipX;
            tiles[16] = 2;
            for (int y = 0; y < 16; y++)
            {
                Array.Clear(transforms, (y * width) + 16, 16);
            }
        }

        // Through the compact JSON the build ships, whose writer escapes base64's '+'.
        SceneDocumentEntry compiled = SceneDocument.Parse(CompiledTiles.Compile(SceneDocument.Parse(Document(width, height, tiles, transforms)), [TileMap.Key]).ToJson()).Entries[0];

        Assert.False(compiled.Authored!.TryGetValue("tiles", out _) || compiled.Authored.TryGetValue("transforms", out _));
        Assert.True(compiled.Authored.TryGetValue(CompiledTiles.Key, out ReadOnlyMemory<byte> blocks));
        CellBlocks cells = CompiledTiles.Read(blocks.Span, width, height, bulk);
        Assert.Equal(CellBlocks.FromDense(width, height, tiles, [.. transforms.Select(static turn => (byte)turn)]).AllocatedBlocks, cells.AllocatedBlocks);
        if (painted && width > 16)
        {
            Assert.Equal((true, false), (cells.BlockAt(0, 0)!.HoldsVariants, cells.BlockAt(1, 0)!.HoldsVariants));
        }

        TileGrid grid = new(SceneFixtures.TileSize, width, height, Palette, cells, SceneFixtures.TerrainAtlas, 4);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal((Palette[tiles[(y * width) + x]], (TileTransform)transforms[(y * width) + x]), (grid.TileAt(x, y), grid.TransformAt(x, y)));
            }
        }
    }

    // Malformed dense cells stay as authored, and the map refuses them at composition naming the defect.
    [Fact]
    public void MalformedDenseCells_AreLeftForTheMapToRefuse()
    {
        SceneDocument authored = SceneDocument.Parse(Document(2, 1, [1], null));

        Assert.Same(authored, CompiledTiles.Compile(authored, [TileMap.Key]));
    }

    // Packed blocks of another version, of the wrong length, or painting past the map's edge are refused, each
    // naming the defect and the rebuild that fixes it. The map is 5 by 3, one block.
    [Theory]
    [InlineData(2, 0, "compiled in format version 2, and this engine reads version 1. Rebuild the assets.")]
    [InlineData(1, 100, "where 1 blocks, 0 of them turned, take 515")]
    [InlineData(1, 5, "lies beyond the map's edge")]
    public void PackedBlocksTheBuildDidNotWrite_AreRefused(byte version, int paintedCell, string defect)
    {
        byte[] layout = new byte[3 + (paintedCell == 100 ? 10 : 512)];
        layout[0] = version;
        layout[1] = 1;
        if (paintedCell < 100)
        {
            layout[3 + (2 * paintedCell)] = 1;
        }

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => CompiledTiles.Read(Encoding.UTF8.GetBytes($"\"{Convert.ToBase64String(layout)}\""), 5, 3));

        Assert.Contains(defect, error.Message, StringComparison.Ordinal);
    }

    // Compiled cells naming no palette entry or no TileTransform are refused at the dense index they stand for.
    [Theory]
    [InlineData(9, 0, "tiles[6] is 9. Use a tileTypes index in 0..3.")]
    [InlineData(1, 8, "transforms[6] is 8. Use a TileTransform in 0..7.")]
    public void CompiledCellsOutsideThePaletteOrTransforms_AreRefused(int value, byte variant, string defect)
    {
        int[] tiles = new int[15];
        byte[] transforms = new byte[15];
        (tiles[6], transforms[6]) = (value, variant);
        CellBlocks cells = CellBlocks.FromDense(5, 3, tiles, transforms);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(SceneFixtures.TileSize, 5, 3, Palette, cells, SceneFixtures.TerrainAtlas, 4));

        Assert.Contains(defect, error.Message, StringComparison.Ordinal);
    }

    private static string Document(int width, int height, int[] tiles, int[]? transforms) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            {"entities": [{"type": "tile-map", "tileSize": 16, "width": {{width}}, "height": {{height}}, "texture": "terrain.png", "columns": 4,
              "tileTypes": [{"name": "empty"}, {"name": "rock", "cell": 0, "layer": "solid"}, {"name": "moss", "cell": 1}, {"name": "vine", "cell": 2}],
              "tiles": [{{string.Join(", ", tiles)}}]{{(transforms is null ? string.Empty : $", \"transforms\": [{string.Join(", ", transforms)}]")}}}]}
            """);
}

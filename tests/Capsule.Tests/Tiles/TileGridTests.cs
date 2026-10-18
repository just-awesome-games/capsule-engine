using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Tiles;

public sealed class TileGridTests
{
    private static readonly TextureHandle Atlas = SceneFixtures.TerrainAtlas;

    // A palette written as "empty", "name" for a tile type with no cell, or "name:cell". Each row breaks
    // one rule of the palette, the area or the texture and columns pairing.
    [Theory]
    [InlineData("ground:0 wall:1", 2, 1, true, 4, "tileTypes[0]")]
    [InlineData("empty ground:0 ground:1", 2, 1, true, 4, "ground")]
    [InlineData("empty", 65536, 65536, false, 0, "requires 4294967296")]
    [InlineData("empty hazard", 2, 1, true, 4, "terrain")]
    [InlineData("empty ground:0", 2, 1, true, 0, "columns")]
    [InlineData("empty hazard", 2, 1, false, 4, "columns is 4")]
    public void Constructor_RejectsABrokenPaletteAreaOrAtlas(string palette, int width, int height, bool textured, int columns, string named)
    {
        TileType[] tileTypes =
        [
            .. palette.Split(' ').Select(static entry => entry.Split(':') switch
            {
                ["empty"] => TileGrid.EmptyTile,
                [string name] => new TileType { Name = name },
                [string name, string cell] => SceneFixtures.Tile(name, int.Parse(cell, CultureInfo.InvariantCulture)),
                _ => throw new ArgumentException($"{entry} is not a palette entry. Write empty, name or name:cell."),
            }),
        ];
        // The overflowing grid is refused before its tiles are counted.
        int[] tiles = width * (long)height == 2 ? [0, 1] : [];

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, width, height, tileTypes, tiles, textured ? Atlas : null, columns));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_AcceptsASemanticTileTypeWithNoCell()
    {
        TileGrid grid = new(16, 2, 1, [TileGrid.EmptyTile, new TileType { Name = "hazard" }], [0, 1]);

        Assert.Equal("hazard", grid.TileTypes[grid.Tiles[1]].Name);
        Assert.Null(grid.TileTypes[1].Cell);
        Assert.Null(grid.Texture);
        Assert.Null(grid.Sprites.Span[1]);
    }

    // A cell is read across a row of Columns and then down, square at the grid's tile size.
    // The whole atlas layout falls out of three numbers the document already carries.
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(2, 32, 0)]
    [InlineData(4, 0, 16)]
    [InlineData(7, 48, 16)]
    public void ACellBecomesASourceRegionOfColumnsAndTileSize(int cell, int expectedX, int expectedY)
    {
        TileGrid grid = new(
            16,
            1,
            1,
            [TileGrid.EmptyTile, SceneFixtures.Tile("ground", cell)],
            [1],
            Atlas,
            4);

        Assert.Equal<Sprite?>(
            new Sprite(Atlas, new TextureRegion(expectedX, expectedY, 16, 16), new Vector2(8, 8)),
            grid.Sprites.Span[1]);
    }

    // The refusal names the cell whatever makes it undrawable: no texture to cut it from, a negative
    // index, or a region far enough down the atlas that its row alone multiplies past int. There the
    // wrapped coordinate would cut from somewhere else rather than fail.
    // Frames, flattened here as cell and ticks pairs, are refused the same way and for their own
    // defects: beside a cell, empty, or held for no ticks.
    [Theory]
    [InlineData(3, null, 0, false, "draws cell 3")]
    [InlineData(-1, null, 4, true, "draws cell -1")]
    [InlineData(int.MaxValue, null, 1, true, "draws cell 2147483647")]
    [InlineData(0, new[] { 1, 8 }, 4, true, "both cell and frames")]
    [InlineData(null, new int[0], 4, true, "has no frames")]
    [InlineData(null, new[] { 1, 0 }, 4, true, "frames[0] is held for 0 ticks")]
    [InlineData(null, new[] { 1, 8, -1, 8 }, 4, true, "frames[1] draws cell -1")]
    [InlineData(null, new[] { 1, 8 }, 0, false, "draws frames but the grid names no texture")]
    public void Constructor_RejectsATileTypeItCannotDraw(int? cell, int[]? frames, int columns, bool textured, string named)
    {
        TileFrame[]? parsed = frames?.Chunk(2).Select(static pair => new TileFrame(pair[0], pair[1])).ToArray();

        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            1,
            1,
            [TileGrid.EmptyTile, new TileType { Name = "ground", Cell = cell, Frames = parsed }],
            [1],
            textured ? Atlas : null,
            columns));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }
}

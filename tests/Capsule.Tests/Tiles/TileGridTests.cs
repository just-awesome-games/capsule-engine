using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Tiles;

public sealed class TileGridTests
{
    private static readonly TextureHandle Atlas = new("terrain", ".png");

    [Fact]
    public void Constructor_RejectsAPaletteThatDoesNotBeginWithEmpty()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Grid([Tile("ground", 0), Tile("wall", 1)], [0, 1]));

        Assert.Contains("tileTypes[0] must be \"empty\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsARepeatedTileTypeName()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Grid([TileGrid.EmptyTile, Tile("ground", 0), Tile("ground", 1)], [0, 1]));

        Assert.Contains("must be unique", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_AcceptsASemanticTileTypeWithNoCell()
    {
        TileGrid grid = new(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("hazard", null)], [0, 1]);

        Assert.Equal("hazard", grid.TileTypeAt(1, 0));
        Assert.Null(grid.TileTypes[1].Cell);
        Assert.Null(grid.Texture);
        Assert.Null(grid.Sprites[1]);
    }

    [Fact]
    public void Constructor_RejectsAGridWhoseAreaOverflowsAnInt()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 65536, 65536, [TileGrid.EmptyTile], []));

        Assert.Contains("requires 4294967296", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TileTypeAt_ReadsTheGridRowMajor()
    {
        TileGrid grid = new(
            16,
            2,
            2,
            [TileGrid.EmptyTile, Tile("ground", 0), Tile("wall", 1)],
            [0, 1, 2, 0],
            Atlas,
            2);

        Assert.Equal("ground", grid.TileTypeAt(1, 0));
        Assert.Equal("wall", grid.TileTypeAt(0, 1));
    }

    // A cell is read across a row of Columns and then down, square at the grid's tile size, so
    // the whole atlas layout falls out of three numbers the document already carries.
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
            [TileGrid.EmptyTile, Tile("ground", cell)],
            [1],
            Atlas,
            4);

        Assert.Equal<Sprite?>(
            new Sprite(Atlas, new TextureRegion(expectedX, expectedY, 16, 16)),
            grid.Sprites[1]);
    }

    [Fact]
    public void Constructor_RejectsACellOnAGridWithNoTexture()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Tile("ground", 3)], [0, 1]));

        Assert.Contains("draws cell 3", error.Message, StringComparison.Ordinal);
        Assert.Contains("names no texture", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsATextureNoTileTypeDrawsFrom()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("hazard", null)], [0, 1], Atlas, 4));

        Assert.Contains("no tile type draws a cell of it", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsATexturedGridWithNoColumns()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Tile("ground", 0)], [0, 1], Atlas, 0));

        Assert.Contains("columns must be at least 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsColumnsWithoutATexture()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("hazard", null)], [0, 1], null, 4));

        Assert.Contains("columns is 4 on a grid that names no texture", error.Message, StringComparison.Ordinal);
    }

    // A cell far enough down the atlas multiplies past int on its row alone, and the wrapped
    // coordinate would cut a region from somewhere else in the texture rather than fail.
    [Fact]
    public void Constructor_RejectsACellWhoseSourceRegionOutrunsATextureCoordinate()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 1, 1, [TileGrid.EmptyTile, Tile("ground", int.MaxValue)], [1], Atlas, 1));

        Assert.Contains("(\"ground\") draws cell 2147483647", error.Message, StringComparison.Ordinal);
        Assert.Contains("further than a texture coordinate reaches", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsANegativeCell()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Grid([TileGrid.EmptyTile, Tile("ground", -1)], [0, 1]));

        Assert.Contains("draws cell -1", error.Message, StringComparison.Ordinal);
    }

    private static TileDefinition Tile(string type, int cell) => new(type, cell);

    private static TileGrid Grid(TileDefinition[] tileTypes, int[] tiles) =>
        new(16, 2, 1, tileTypes, tiles, Atlas, 4);
}

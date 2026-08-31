using Capsule.Rendering;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Tiles;

public sealed class TileGridTests
{
    private static readonly ColorRgba Slate = new(0x4A, 0x55, 0x68);

    [Fact]
    public void Constructor_RejectsAPaletteThatDoesNotBeginWithEmpty()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 2, 1, [Tile("ground"), Tile("wall")], [0, 1]));

        Assert.Contains("tileTypes[0] must be \"empty\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsARepeatedTileTypeName()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Tile("ground"), Tile("ground")], [0, 1]));

        Assert.Contains("must be unique", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_AcceptsATileTypeWithNoColour()
    {
        TileGrid grid = new(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("ground", null)], [0, 1]);

        Assert.Equal("ground", grid.TileTypeAt(1, 0));
        Assert.Null(grid.TileTypes[1].Color);
    }

    [Fact]
    public void Constructor_AcceptsTheReservedEmptyEntryWithoutOne()
    {
        TileGrid grid = new(16, 2, 1, [TileGrid.EmptyTile, Tile("ground")], [0, 1]);

        Assert.Null(grid.TileTypes[0].Color);
        Assert.Equal(Slate, grid.TileTypes[1].Color);
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
        TileGrid grid = new(16, 2, 2, [TileGrid.EmptyTile, Tile("ground"), Tile("wall")], [0, 1, 2, 0]);

        Assert.Equal("ground", grid.TileTypeAt(1, 0));
        Assert.Equal("wall", grid.TileTypeAt(0, 1));
    }

    private static TileDefinition Tile(string type) => new(type, Slate);
}

using Capsule.Maps;
using Capsule.Rendering;

namespace Capsule.Tests.Maps;

/// <summary>
/// The grid's own contract, held without a map or a file anywhere: a grid built in code is
/// validated the same way one read from disk is.
/// </summary>
public sealed class TileGridTests
{
    private static readonly ColorRgba Slate = new(0x4A, 0x55, 0x68);

    // Index 0 is the format's one reserved slot: every unpainted cell points at it.
    [Fact]
    public void Constructor_RejectsAPaletteThatDoesNotBeginWithEmpty()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => new TileGrid(16, 2, 1, [Tile("ground"), Tile("wall")], [0, 1]));

        Assert.Contains("tileTypes[0] must be \"empty\"", error.Message, StringComparison.Ordinal);
    }

    // A repeated name makes TileTypeAt ambiguous, and the importer's bijectivity rule assumes
    // this cannot happen in a file either.
    [Fact]
    public void Constructor_RejectsARepeatedTileTypeName()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Tile("ground"), Tile("ground")], [0, 1]));

        Assert.Contains("must be unique", error.Message, StringComparison.Ordinal);
    }

    // A tile is authored with its appearance, so one arriving without a colour is a malformed
    // grid rather than a tile the renderer has to guess at.
    [Fact]
    public void Constructor_RejectsATileTypeWithNoColour()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("ground", null)], [0, 1]));

        Assert.Contains("\"ground\" has no colour", error.Message, StringComparison.Ordinal);
    }

    // The reserved entry is the one exception: it is never drawn, so it has nothing to look like.
    [Fact]
    public void Constructor_AcceptsTheReservedEmptyEntryWithoutOne()
    {
        TileGrid grid = new(16, 2, 1, [TileGrid.EmptyTile, Tile("ground")], [0, 1]);

        Assert.Null(grid.TileTypes[0].Color);
        Assert.Equal(Slate, grid.TileTypes[1].Color);
    }

    // Width * Height as an int wraps: 65536 x 65536 is 0, which an empty tiles array would have
    // satisfied, leaving every TileAt on the grid to throw instead.
    [Fact]
    public void Constructor_RejectsAGridWhoseAreaOverflowsAnInt()
    {
        MapFormatException error = Assert.Throws<MapFormatException>(
            () => new TileGrid(16, 65536, 65536, [TileGrid.EmptyTile], []));

        Assert.Contains("requires 4294967296", error.Message, StringComparison.Ordinal);
    }

    // Row-major is a contract a reader cannot check: a transposed grid still has the right
    // number of tiles and still validates.
    [Fact]
    public void TileTypeAt_ReadsTheGridRowMajor()
    {
        TileGrid grid = new(16, 2, 2, [TileGrid.EmptyTile, Tile("ground"), Tile("wall")], [0, 1, 2, 0]);

        Assert.Equal("ground", grid.TileTypeAt(1, 0));
        Assert.Equal("wall", grid.TileTypeAt(0, 1));
    }

    private static TileDefinition Tile(string type) => new(type, Slate);
}

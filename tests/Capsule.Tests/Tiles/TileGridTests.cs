using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Tiles;

public sealed class TileGridTests
{
    private static readonly TextureHandle Atlas = SceneFixtures.TerrainAtlas;

    [Fact]
    public void Constructor_RejectsAPaletteThatDoesNotBeginWithEmpty()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Grid([SceneFixtures.Tile("ground", 0), SceneFixtures.Tile("wall", 1)], [0, 1]));

        Assert.Contains("tileTypes[0]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsARepeatedTileTypeName()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Grid([TileGrid.EmptyTile, SceneFixtures.Tile("ground", 0), SceneFixtures.Tile("ground", 1)], [0, 1]));

        Assert.Contains("ground", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_AcceptsASemanticTileTypeWithNoCell()
    {
        TileGrid grid = new(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("hazard", null)], [0, 1]);

        Assert.Equal("hazard", grid.TileTypes[grid.Tiles[1]].Type);
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
            [TileGrid.EmptyTile, SceneFixtures.Tile("ground", cell)],
            [1],
            Atlas,
            4);

        Assert.Equal<Sprite?>(
            new Sprite(Atlas, new TextureRegion(expectedX, expectedY, 16, 16), new Vector2(8, 8)),
            grid.Sprites[1]);
    }

    [Fact]
    public void Constructor_RejectsATextureNoTileTypeDrawsFrom()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("hazard", null)], [0, 1], Atlas, 4));

        Assert.Contains("terrain", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsATexturedGridWithNoColumns()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, SceneFixtures.Tile("ground", 0)], [0, 1], Atlas, 0));

        Assert.Contains("columns", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsColumnsWithoutATexture()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(16, 2, 1, [TileGrid.EmptyTile, new TileDefinition("hazard", null)], [0, 1], null, 4));

        Assert.Contains("columns is 4", error.Message, StringComparison.Ordinal);
    }

    // Whatever makes a cell undrawable — no texture to cut it from, a negative index, or a region
    // far enough down the atlas that its row alone multiplies past int, where the wrapped
    // coordinate would cut from somewhere else rather than fail — the refusal names the cell.
    [Theory]
    [InlineData(3, 0, false, "draws cell 3")]
    [InlineData(-1, 4, true, "draws cell -1")]
    [InlineData(int.MaxValue, 1, true, "draws cell 2147483647")]
    public void Constructor_RejectsACellItCannotDraw(int cell, int columns, bool textured, string named)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            1,
            1,
            [TileGrid.EmptyTile, SceneFixtures.Tile("ground", cell)],
            [1],
            textured ? Atlas : null,
            columns));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    private static TileGrid Grid(TileDefinition[] tileTypes, int[] tiles) =>
        new(16, 2, 1, tileTypes, tiles, Atlas, 4);
}

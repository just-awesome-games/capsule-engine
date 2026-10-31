using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Tiles;

// A grid keeps only its painted cells, in blocks of 16 by 16, and reads back exactly the dense grid it was built from.
public sealed class TileStorageTests
{
    // 37 by 21 cuts a partial block on the right and the bottom.
    private const int Wide = 37;
    private const int High = 21;

    // The palette every storage fixture paints from.
    internal static readonly TileType[] Palette =
        [TileGrid.EmptyTile, SceneFixtures.Tile("rock", 0, "solid"), SceneFixtures.Tile("moss", 1), SceneFixtures.Tile("vine", 2)];

    // Built from a scene document's arrays or from code, the grid and a map of it read every cell as the dense
    // input, across every block boundary and into both partial blocks.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGridReadsBackItsDenseInput_CellForCell(bool authored)
    {
        (int[] tiles, int[] transforms) = Scattered(Wide, High);
        TileGrid grid = authored
            ? new TileGrid(SceneFixtures.TileSize, Wide, High, Palette, tiles, SceneFixtures.TerrainAtlas, 4, (ReadOnlyMemory<int>?)transforms)
            : new TileGrid(SceneFixtures.TileSize, Wide, High, Palette, tiles, SceneFixtures.TerrainAtlas, 4, [.. transforms.Select(static turn => (TileTransform)turn)]);
        TileMap map = new(grid);

        for (int y = 0; y < High; y++)
        {
            for (int x = 0; x < Wide; x++)
            {
                (TileType, TileTransform) expected = (Palette[tiles[(y * Wide) + x]], (TileTransform)transforms[(y * Wide) + x]);
                Assert.Equal(expected, (grid.TileAt(x, y), grid.TransformAt(x, y)));
                Assert.Equal(expected, (map.TileAt(x, y), map.TransformAt(x, y)));
            }
        }
    }

    // One painted cell allocates its block alone, and a block keeps transforms only once a cell in it turns.
    [Fact]
    public void OnlyAPaintedBlockIsAllocated_AndOnlyATurnedOneHoldsTransforms()
    {
        int[] tiles = new int[Wide * High];
        int[] transforms = new int[Wide * High];
        tiles[(3 * Wide) + 17] = 1;
        tiles[(20 * Wide) + 36] = 2;
        transforms[(20 * Wide) + 36] = (int)TileTransform.FlipY;

        CellBlocks cells = new TileGrid(SceneFixtures.TileSize, Wide, High, Palette, tiles, SceneFixtures.TerrainAtlas, 4, (ReadOnlyMemory<int>?)transforms).Cells;

        Assert.Equal(2, cells.AllocatedBlocks);
        Assert.False(cells.BlockAt(1, 0)!.HoldsVariants);
        Assert.True(cells.BlockAt(2, 1)!.HoldsVariants);
        Assert.Equal(0, new TileGrid(SceneFixtures.TileSize, Wide, High, Palette, new int[Wide * High], SceneFixtures.TerrainAtlas, 4).Cells.AllocatedBlocks);
    }

    // Painting an empty block reads and collides as painted, and clearing a block's last cell releases it. The grid
    // the map was built from reads as authored throughout.
    [Fact]
    public void SetTile_IntoAnEmptyBlock_AndRemoveTile_OfABlocksLastCell_EditOnlyTheMap()
    {
        string[] rows = [.. Enumerable.Repeat(new string('.', 20), 18)];
        rows[2] = "#" + rows[2][1..];
        TileGrid grid = SceneFixtures.TerrainGrid(rows);
        TileMap map = SceneFixtures.Colliding(grid);
        Scene scene = new Scene().Started();
        scene.Add(map);

        map.SetTile(19, 17, "solid");
        map.RemoveTile(0, 2);

        Assert.Equal(("solid", TileGrid.EmptyTileName), (map.TileAt(19, 17).Name, map.TileAt(0, 2).Name));
        Assert.Equal(1, map.Cells.AllocatedBlocks);
        Assert.True(scene.Collision.Raycast(new Vector2(312f, 260f), Vector2.UnitY, 32f, CollisionFilter.Everything, out _));
        Assert.False(scene.Collision.Raycast(new Vector2(8f, 0f), Vector2.UnitY, 64f, CollisionFilter.Everything, out _));
        Assert.Equal(("solid", TileGrid.EmptyTileName), (grid.TileAt(0, 2).Name, grid.TileAt(19, 17).Name));
        Assert.Equal(1, grid.Cells.AllocatedBlocks);
    }

    // A map whose grid has no texture, such as a hidden collision layer, has no renderer to walk.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMapDrawsOnlyWithATexture(bool textured)
    {
        TileGrid grid = textured
            ? new TileGrid(SceneFixtures.TileSize, 2, 1, [TileGrid.EmptyTile, SceneFixtures.Tile("rock", 0, "solid")], [0, 1], SceneFixtures.TerrainAtlas, 1)
            : new TileGrid(SceneFixtures.TileSize, 2, 1, [TileGrid.EmptyTile, new TileType { Name = "rock", Layer = "solid" }], [0, 1]);

        Assert.Equal(textured, new TileMap(grid).Has<Renderer>());
    }

    [Fact]
    public void APaletteBeyondWhatACellIndexes_IsRefused()
    {
        TileType[] palette = [TileGrid.EmptyTile, .. Enumerable.Range(1, CellBlocks.MaxValue + 1).Select(static index => new TileType { Name = $"t{index}" })];

        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(SceneFixtures.TileSize, 1, 1, palette, [0]));

        Assert.Contains("tileTypes has 65537 entries, more than the 65536 a grid can index", error.Message, StringComparison.Ordinal);
    }

    // About one cell in eight painted, a quarter of those turned, from a fixed seed.
    internal static (int[] Tiles, int[] Transforms) Scattered(int width, int height)
    {
        Random random = new(16);
        int[] tiles = new int[width * height];
        int[] transforms = new int[width * height];
        for (int index = 0; index < tiles.Length; index++)
        {
            if (random.Next(8) == 0)
            {
                tiles[index] = random.Next(1, Palette.Length);
                transforms[index] = random.Next(4) == 0 ? random.Next(1, TileTransforms.Count) : 0;
            }
        }

        return (tiles, transforms);
    }
}

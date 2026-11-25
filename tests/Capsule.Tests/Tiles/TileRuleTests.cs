using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Tiles;

public sealed class TileRuleTests
{
    // "wall" draws cell n where the sides whose bits sum to n join it. "stone" joins a wall without being one, and
    // "dirt" joins nothing. "rail" reads only its left and right, and "nook" its up, right and the corner between.
    internal static readonly TileType[] Palette =
    [
        TileGrid.EmptyTile,
        new TileType { Name = "wall", Layer = "solid", Terrain = "wall", Rules = [.. Enumerable.Range(0, 16).Select(static joined => new TileRule(joined, (TileSides)joined))] },
        new TileType { Name = "stone", Cell = 20, Layer = "solid", Terrain = "wall" },
        new TileType { Name = "dirt", Cell = 21 },
        new TileType
        {
            Name = "rail",
            Terrain = "rail",
            Rules = [new(30), new(31, TileSides.Left), new(32, TileSides.Right), new(33, TileSides.Left | TileSides.Right)],
        },
        new TileType
        {
            Name = "nook",
            Terrain = "nook",
            Rules =
            [
                new(40),
                new(41, TileSides.Up),
                new(42, TileSides.Right),
                new(43, TileSides.Up | TileSides.Right),
                new(44, TileSides.Up | TileSides.Right | TileSides.UpRight),
            ],
        },
    ];

    private const string Tiles = ".wsdrn";

    // Rows of a map split by spaces, one character a cell from Tiles, and the cell the middle one draws.
    [Theory]
    [InlineData("... .w. ...", 0)]
    [InlineData(".w. .w. ...", 1)]
    [InlineData("... sws ...", 10)]
    [InlineData("ddd dwd ddd", 0)]
    [InlineData("w", 15)]
    [InlineData(".r. .r. .r.", 30)]
    [InlineData("... rrr ...", 33)]
    [InlineData("..n .n. ...", 40)]
    [InlineData(".n. .nn ...", 43)]
    [InlineData(".nn .nn ...", 44)]
    public void ACellDrawsTheRuleNamingExactlyTheSidesThatJoinIt(string rows, int cell)
    {
        int[] tiles = Parse(rows, out int width, out int height);

        // Decoded cells are shared by every composition of a document, and keep the entries they were authored with.
        CellBlocks decoded = CellBlocks.FromDense(width, height, tiles, []);
        foreach (TileGrid grid in new[]
        {
            new TileGrid(8, width, height, Palette, tiles, SceneFixtures.Atlas, 8),
            new TileGrid(8, width, height, Palette, decoded, SceneFixtures.Atlas, 8),
        })
        {
            TileMap map = new(grid);

            Assert.Equal(cell, LookOf(map, width / 2, height / 2));
            Assert.Same(Palette[tiles[tiles.Length / 2]], map.TileAt(width / 2, height / 2));
            Assert.Equal(tiles[tiles.Length / 2], decoded.ValueAt(width / 2, height / 2));
        }
    }

    // Random paints on a map of every kind of tile, spanning four blocks. After each, only cells beside the painted
    // one have changed, and every look and collision layer is the one a map built whole from the same tiles holds,
    // however the cells came to hold them.
    [Fact]
    public void APaintRepicksOnlyTheCellsAroundIt_AsIfTheMapWereBuiltWhole()
    {
        const int Size = 20;
        Random random = new(7);
        int[] tiles = [.. Enumerable.Range(0, Size * Size).Select(_ => random.Next(Palette.Length))];
        TileMap map = Placed(tiles, Size);

        for (int paint = 0; paint < 400; paint++)
        {
            int[] before = Slots(map);
            (int x, int y, int tile) = (random.Next(Size), random.Next(Size), random.Next(Palette.Length));
            tiles[(y * Size) + x] = tile;

            map.SetTile(x, y, Palette[tile].Name);

            int[] after = Slots(map);
            for (int cell = 0; cell < after.Length; cell++)
            {
                Assert.True(before[cell] == after[cell] || Math.Max(Math.Abs((cell % Size) - x), Math.Abs((cell / Size) - y)) <= 1, $"paint {paint} at ({x}, {y}) changed cell {cell}");
            }

            TileMap whole = Placed(tiles, Size);
            Assert.Equal(Slots(whole), after);
            Assert.Equal(Layers(whole), Layers(map));
            Assert.All(Enumerable.Range(0, after.Length), cell => Assert.Equal(map.TileAt(cell % Size, cell / Size).Layer is not null, Layers(map)[cell] != 0));
        }
    }

    // Two rules for one combination, reading no side: each cell keeps the same pick in any map and any run, and a row
    // uses both.
    [Fact]
    public void RulesSharingACombination_ArePickedByPosition()
    {
        TileType[] palette = [TileGrid.EmptyTile, new TileType { Name = "moss", Rules = [new(1), new(2)] }];
        int[] tiles = [.. Enumerable.Repeat(1, 32)];

        int[] first = Looks(new TileMap(new TileGrid(8, 32, 1, palette, tiles, SceneFixtures.Atlas, 4)));
        TileMap painted = new(new TileGrid(8, 32, 1, palette, new int[32], SceneFixtures.Atlas, 4));
        for (int x = 31; x >= 0; x--)
        {
            painted.SetTile(x, 0, "moss");
        }

        Assert.Equal(first, Looks(painted));
        Assert.Equal([1, 2], first.Distinct().Order());
    }

    // Each row breaks one rule of a rule tile, named by what the message says.
    [Theory]
    [InlineData("missing", "no rule for \"up, left\" joined")]
    [InlineData("cell", "sets both cell and rules")]
    [InlineData("unnamed", "neither joins nor terrain")]
    [InlineData("stranger", "which no tile type's terrain is")]
    [InlineData("corner", "naming a corner without both its sides")]
    [InlineData("joinsAlone", "has no rules")]
    [InlineData("turned", "drop its transform")]
    public void AGrid_RefusesAMalformedRuleTile(string defect, string named)
    {
        TileType entry = defect switch
        {
            "missing" => new TileType { Name = "bad", Terrain = "t", Rules = [new(0), new(1, TileSides.Up), new(2, TileSides.Left)] },
            "cell" => new TileType { Name = "bad", Terrain = "t", Cell = 0, Rules = [new(0)] },
            "unnamed" => new TileType { Name = "bad", Rules = [new(0), new(1, TileSides.Up)] },
            "stranger" => new TileType { Name = "bad", Joins = "u", Terrain = "t", Rules = [new(0)] },
            "corner" => new TileType { Name = "bad", Terrain = "t", Rules = [new(0), new(1, TileSides.UpLeft)] },
            "joinsAlone" => new TileType { Name = "bad", Cell = 0, Joins = "t" },
            _ => new TileType { Name = "bad", Terrain = "t", Rules = [new(0)] },
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new TileGrid(8, 1, 1, [TileGrid.EmptyTile, entry], [1], SceneFixtures.Atlas, 4, defect == "turned" ? [TileTransform.FlipX] : null));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleTileIsPaintedWithoutATransform()
    {
        TileMap map = new(new TileGrid(8, 1, 1, Palette, [0], SceneFixtures.Atlas, 8));

        Assert.Throws<ArgumentException>(() => map.SetTile(0, 0, "wall", TileTransform.FlipX));
    }

    internal static int LookOf(TileMap map, int x, int y) => map.Grid.Rules!.LookCell(map.Cells.ValueAt(x, y));

    private static TileMap Placed(int[] tiles, int size)
    {
        TileMap map = SceneFixtures.Colliding(new TileGrid(8, size, size, Palette, tiles, SceneFixtures.Atlas, 8));
        new Scene().Started().Add(map);

        return map;
    }

    private static int[] Layers(TileMap map)
    {
        CollisionGrid2D grid = map.Get<TileMapCollider2D>().Grid!;

        return [.. Enumerable.Range(0, map.Width * map.Height).Select(cell => grid.LayerOf(cell % map.Width, cell / map.Width).Index)];
    }

    private static int[] Parse(string rows, out int width, out int height)
    {
        string[] lines = rows.Split(' ');
        (width, height) = (lines[0].Length, lines.Length);

        return [.. string.Concat(lines).Select(static tile => Tiles.IndexOf(tile, StringComparison.Ordinal))];
    }

    private static int[] Slots(TileMap map) =>
        [.. Enumerable.Range(0, map.Width * map.Height).Select(cell => map.Cells.ValueAt(cell % map.Width, cell / map.Width))];

    private static int[] Looks(TileMap map) => [.. Enumerable.Range(0, map.Width).Select(x => LookOf(map, x, 0))];
}

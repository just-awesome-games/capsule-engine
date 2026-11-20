using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Tiles;

// A view that retains its tiles draws, through the chunks a host keeps, the instances a view that writes every tile
// draws. A chunk's cells outside the camera's bounds draw nothing a frame shows, and only the order of one map's
// tiles among themselves may differ.
public sealed class TileChunkTests
{
    private const int TileSize = 8;

    private static readonly TextureSlice Page = Slice(3, 5);

    [Fact]
    public void RetainedChunks_DrawTheTiles_AsTheViewScrollsAcrossChunks_AndPastTheCap()
    {
        Frames frames = new(Grid(320, 256));
        for (int step = 0; step < 40; step++)
        {
            frames.Step((scene, _) => scene.Camera.Center = new Vector2(230f + (step * 7f), 180f + (step * 5f)));
            frames.AssertSame();
        }

        // Into each of the map's 80 chunks in turn, past the 64 kept.
        for (int chunk = 0; chunk < 80; chunk++)
        {
            Vector2 centre = (new Vector2(chunk % 10, chunk / 10) + new Vector2(0.5f)) * TileRun.ChunkSize * TileSize;
            frames.Step((scene, _) => scene.Camera.Teleport(centre));
            frames.AssertSame();
        }

        Assert.Equal(64, frames.Chunks.Count);
    }

    // A painted cell, a cell cleared until its block empties, and a block emptied and painted anew in one step.
    [Fact]
    public void EditedCells_DrawTheirNewTiles_WhereABlockEmptiesAndWhereItFillsAgain()
    {
        Frames frames = new(Grid(64, 48));
        frames.Step();
        frames.AssertSame();

        frames.Step((_, map) => map.SetTile(5, 5, "ledge"));
        frames.AssertSame();

        // The lone cell of block (2, 1).
        frames.Step((_, map) => map.RemoveTile(40, 20));
        frames.AssertSame();

        frames.Step((_, map) => map.SetTile(41, 21, "rock"));
        frames.AssertSame();

        frames.Step((_, map) =>
        {
            map.RemoveTile(41, 21);
            map.SetTile(36, 18, "ledge", TileTransform.FlipX);
        });
        frames.AssertSame();
    }

    // A chunk holding an animated entry is written again as its frame changes, and one holding none is kept.
    [Fact]
    public void AnimatedTiles_DrawTheirFrames_AndAChunkWithoutOneKeepsItsInstances()
    {
        Frames frames = new(Grid(64, 32, falls: true));
        frames.Step();
        frames.AssertSame();
        SpriteInstance[] still = frames.Kept[(1, 0)];
        for (int step = 0; step < 6; step++)
        {
            frames.Step();
            frames.AssertSame();
        }

        Assert.Same(still, frames.Kept[(1, 0)]);
    }

    // A changing tint or flash writes every tile, as a chunk would be written again each step, and a steady one is
    // kept under the style it was written with.
    [Fact]
    public void AMapFadingOrFlashing_WritesItsTiles_AndASteadyStyleIsRetained()
    {
        Frames frames = new(Grid(64, 48));
        frames.Step();
        for (int step = 1; step <= 4; step++)
        {
            frames.Step((_, map) => map.Tint = new ColorRgba(255, 255, 255, (byte)(255 - (step * 40))));
            frames.AssertSame();
            Assert.Empty(frames.Retained.TileRuns.ToArray());
        }

        frames.Step();
        frames.AssertSame();
        frames.Step((_, map) =>
        {
            map.FlashColor = new ColorRgba(200, 40, 10);
            map.Flash = 0.5f;
        });
        frames.AssertSame();
        frames.Step();
        frames.AssertSame();

        Assert.NotEmpty(frames.Retained.TileRuns.ToArray());
    }

    [Fact]
    public void AChunkDrawnFromAnotherTexturePage_IsWrittenOnIt()
    {
        Frames frames = new(Grid(64, 48));
        frames.Step();
        frames.AssertSame();

        frames.AssertSame(Slice(3, 5));
        frames.AssertSame(Slice(0, 9, Page.Texture));
    }

    // A removed map draws nothing, and its chunks let go of it once no frame has drawn them for a while.
    [Fact]
    public void ARemovedMap_DrawsNothing_AndItsChunksLetGoOfIt()
    {
        Frames frames = new(Grid(64, 48));
        frames.Step();
        frames.AssertSame();

        frames.Step((scene, map) => scene.Remove(map));
        frames.AssertSame();
        for (int frame = 0; frame < 2048; frame++)
        {
            frames.Chunks.BeginFrame();
        }

        Assert.Empty(frames.Retained.TileRuns.ToArray());
        Assert.Equal(0, frames.Chunks.Count);
    }

    // A view reaching 40 chunks keeps 8 more on each frame, and draws the cells it shows of the rest from their tiles.
    [Fact]
    public void AFrameReachingMoreNewChunksThanItKeeps_DrawsTheRestFromTheirTiles_UntilItKeepsThemAll()
    {
        Frames frames = new(Grid(320, 256), new Vector2(1000f, 600f), new Vector2(2000f, 1200f));
        frames.Step();
        List<int> shown = [];
        for (int frame = 0; frame < 6; frame++)
        {
            frames.AssertSame();
            shown.Add(frames.Shown);
        }

        Assert.Equal([32, 24, 16, 8, 0, 0], shown);
    }

    // A scene boundary lets go of every chunk, and the view the scene returned holds none of its maps either.
    [Fact]
    public void ForgettingTheChunks_LetsGoOfTheirMaps()
    {
        TileChunks chunks = new(checks: false);
        ViewLender lender = new() { RetainsDrawState = true };
        WeakReference map = Drawn(chunks, lender);

        chunks.Forget();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(0, chunks.Count);
        Assert.False(map.IsAlive);
        GC.KeepAlive(lender);
    }

    [Fact]
    public void TheCheck_Throws_WhereAKeptChunkNoLongerDrawsWhatItsTilesDo()
    {
        Shifting source = new();
        TileChunks chunks = new(checks: true);
        TileRun run = new(source, 0, 0, Cells: 1, Frames: 0, 0, 0, 1, 1);
        chunks.BeginFrame();
        chunks.Resolve(in run, ColorRgba.White, default, in Page);
        chunks.BeginFrame();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => chunks.Resolve(in run, ColorRgba.White, default, in Page));

        Assert.Contains("(0, 0)", thrown.Message, StringComparison.Ordinal);
    }

    // A grid of rock and ledge in four facings, one cell in sixteen empty, where block (2, 1) holds one cell and the
    // chunk at (1, 0) none that animates. With falls, the first chunk holds some cells of an animated entry.
    internal static TileGrid Grid(int width, int height, bool falls = false)
    {
        int[] tiles = new int[width * height];
        TileTransform[] transforms = new TileTransform[tiles.Length];
        for (int cell = 0; cell < tiles.Length; cell++)
        {
            int x = cell % width;
            int y = cell / width;
            int mix = (cell * 31) + (y * 13);
            bool lone = x >> 4 == 2 && y >> 4 == 1;
            tiles[cell] = lone ? (x == 40 && y == 20 ? 1 : 0) : mix % 16 == 0 ? 0 : 1 + (mix % 2);
            tiles[cell] = falls && x < 32 && mix % 5 == 0 && tiles[cell] != 0 ? 3 : tiles[cell];
            transforms[cell] = tiles[cell] == 0 ? TileTransform.None : (TileTransform)(mix / 2 % 4);
        }

        return new TileGrid(
            TileSize,
            width,
            height,
            [
                TileGrid.EmptyTile,
                new TileType { Name = "rock", Cell = 0 },
                new TileType { Name = "ledge", Cell = 1 },
                new TileType { Name = "falls", Frames = [new(1, 2), new(2, 3)] },
            ],
            tiles,
            SceneFixtures.Atlas,
            4,
            transforms);
    }

    // A map's chunks resolved into chunks by a retained frame of a scene that is then gone.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Drawn(TileChunks chunks, ViewLender lender)
    {
        Scene scene = new();
        SceneFixtures.Open(scene, new Vector2(230f, 180f), new Vector2(200f, 120f));
        TileMap map = new(Grid(64, 48));
        scene.Add(map);
        using (SceneSimulation simulation = new(scene, null, null, lender))
        {
            simulation.Step(SceneFixtures.Step());
            FrameView view = simulation.View;
            chunks.BeginFrame();
            foreach (StoredPolygon polygon in view.Polygons)
            {
                chunks.Resolve(in view.TileRuns[polygon.TileRunIndex], polygon.Color, polygon.Flash, in Page);
            }
        }

        Assert.NotEqual(0, chunks.Count);

        return new WeakReference(map);
    }

    // A texture page standing in for one on the device, with the source's texture at offsetX, offsetY on it.
    internal static TextureSlice Slice(int offsetX, int offsetY, SampledTexture? page = null) =>
        new(page ?? new SampledTexture(new GpuTexture(0, 0, GpuTextureFormat.R8G8B8A8Unorm, 64, 64)), offsetX, offsetY);

    // The same scene twice, one drawn through a view that retains its tiles and one through a view that writes them,
    // each with its map between a sprite under it and one over it.
    internal sealed class Frames
    {
        private readonly (Scene Scene, TileMap Map, SceneSimulation Simulation) _expanded;
        private readonly (Scene Scene, TileMap Map, SceneSimulation Simulation) _retained;
        private long _tick;

        internal TileChunks Chunks { get; } = new(checks: true);

        // The instances each chunk the last frame drew holds, by its place.
        internal Dictionary<(int X, int Y), SpriteInstance[]> Kept { get; } = [];

        // How many chunks the last frame drew from their tiles, having no room to keep them yet.
        internal int Shown { get; private set; }

        internal Frames(TileGrid grid, Vector2? centre = null, Vector2? viewport = null)
        {
            _expanded = Build(grid, null, centre ?? new Vector2(230f, 180f), viewport ?? new Vector2(200f, 120f));
            _retained = Build(grid, new ViewLender { RetainsDrawState = true }, centre ?? new Vector2(230f, 180f), viewport ?? new Vector2(200f, 120f));
        }

        internal FrameView Retained => _retained.Simulation.View;

        // Changes both scenes the same way, then steps them.
        internal void Step(Action<Scene, TileMap>? change = null)
        {
            change?.Invoke(_expanded.Scene, _expanded.Map);
            change?.Invoke(_retained.Scene, _retained.Map);
            _expanded.Simulation.Step(SceneFixtures.Step(_tick));
            _retained.Simulation.Step(SceneFixtures.Step(_tick));
            _tick++;
        }

        internal void AssertSame(TextureSlice? slice = null)
        {
            TextureSlice page = slice ?? Page;
            Chunks.BeginFrame();
            Kept.Clear();
            Shown = 0;

            Assert.Equal(Drawn(_expanded.Simulation.View, page), Drawn(Retained, page));
        }

        private static (Scene, TileMap, SceneSimulation) Build(TileGrid grid, ViewLender? lender, Vector2 centre, Vector2 viewport)
        {
            Scene scene = new();
            SceneFixtures.Open(scene, centre, viewport);
            TileMap map = new(grid);
            scene.Add(Holding(new Vector2(200f, 150f), -1));
            scene.Add(map);
            scene.Add(Holding(new Vector2(260f, 170f), 1));

            return (scene, map, new SceneSimulation(scene, null, null, lender));
        }

        private static EntityHierarchyFixtures.Node Holding(Vector2 position, int z)
        {
            EntityHierarchyFixtures.Node node = new(position) { ZIndex = z };
            node.Add(new SpriteRenderer(SceneFixtures.Frame(5, 3)));

            return node;
        }

        // Each instance the view draws as hex, in draw order. A run of one map's tiles is cut to the camera's bounds
        // and sorted by cell.
        private List<string> Drawn(FrameView view, TextureSlice page)
        {
            List<string> drawn = [];
            List<SpriteInstance> tiles = [];
            Rect bounds = view.Camera.SweptBounds;
            ReadOnlySpan<SpriteIntent> sprites = view.Sprites;
            ReadOnlySpan<StoredPolygon> polygons = view.Polygons;
            int polygon = 0;
            for (int index = 0; index <= sprites.Length; index++)
            {
                for (; polygon < polygons.Length && polygons[polygon].Before <= index; polygon++)
                {
                    Assert.True(polygons[polygon].HoldsTiles);
                    TileRun run = view.TileRuns[polygons[polygon].TileRunIndex];
                    if (Chunks.Resolve(in run, polygons[polygon].Color, polygons[polygon].Flash, in page) is not { } chunk)
                    {
                        ReadOnlySpan<SpriteIntent> shown = Chunks.WriteShown(in run, polygons[polygon].Color, polygons[polygon].Flash);
                        SpriteInstance[] written = new SpriteInstance[shown.Length];
                        SpriteDrawList.Write(shown, written, page.OffsetX, page.OffsetY);
                        tiles.AddRange(written);
                        Shown++;
                        continue;
                    }

                    Kept[(run.X, run.Y)] = chunk.Kept!;
                    tiles.AddRange(chunk.Kept!.Where(tile => Within(tile, bounds)));
                }

                if (index == sprites.Length)
                {
                    break;
                }

                SpriteInstance[] instance = new SpriteInstance[1];
                SpriteDrawList.Write(sprites.Slice(index, 1), instance, page.OffsetX, page.OffsetY);
                if (sprites[index].Size == new Vector2(TileSize))
                {
                    tiles.Add(instance[0]);
                    continue;
                }

                Flush(tiles, drawn);
                drawn.Add(Hex(instance[0]));
            }

            Flush(tiles, drawn);

            return drawn;
        }

        private static bool Within(in SpriteInstance tile, Rect bounds) =>
            tile.Position.X - (TileSize / 2f) < bounds.Right && tile.Position.X + (TileSize / 2f) > bounds.Left
            && tile.Position.Y - (TileSize / 2f) < bounds.Bottom && tile.Position.Y + (TileSize / 2f) > bounds.Top;

        private static void Flush(List<SpriteInstance> tiles, List<string> drawn)
        {
            drawn.AddRange(tiles.OrderBy(static tile => tile.Position.Y).ThenBy(static tile => tile.Position.X).Select(static tile => Hex(tile)));
            tiles.Clear();
        }

        private static string Hex(SpriteInstance instance) => Convert.ToHexString(MemoryMarshal.AsBytes(new ReadOnlySpan<SpriteInstance>(in instance)));
    }

    // A source whose every write draws its one tile from further along the texture, under the same stamps.
    private sealed class Shifting : ITileSource
    {
        private int _writes;

        public TextureHandle Texture => SceneFixtures.Atlas;

        public int Write(int minX, int minY, int maxX, int maxY, Span<SpriteIntent> sprites, out bool animated)
        {
            animated = false;
            sprites[0] = new SpriteIntent(new Sprite(SceneFixtures.Atlas, new TextureRegion(8 * _writes++, 0, 8, 8)), Vector2.One, Vector2.One, 0f, 0f, new Vector2(TileSize), false, false, ColorRgba.White);

            return 1;
        }
    }
}

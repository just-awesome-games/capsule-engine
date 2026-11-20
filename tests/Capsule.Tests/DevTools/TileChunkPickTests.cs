using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.DevTools.Overlay;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tests.Tiles;
using Capsule.Tiles;

namespace Capsule.Tests.DevTools;

public sealed class TileChunkPickTests
{
    // A held frame that keeps a map's tiles in chunks picks the map on a tile the chunk draws, and nowhere else in the
    // chunk's rect. Cell (40, 20) is the lone tile of its block, and (41, 21) is empty.
    [Theory]
    [InlineData(40, 20, true)]
    [InlineData(41, 21, false)]
    public void APick_FindsAMapKeptInChunks_OnlyOnATileItDraws(int x, int y, bool hit)
    {
        Scene scene = new();
        SceneFixtures.Open(scene, new Vector2(230f, 180f), new Vector2(200f, 120f));
        TileMap map = new(TileChunkTests.Grid(64, 48));
        scene.Add(map);
        using SceneSimulation simulation = new(scene, null, null, new ViewLender { RetainsDrawState = true });
        List<DrawRun> runs = [];
        simulation.DrawRuns = runs;
        simulation.Step(SceneFixtures.Step());
        FrameView view = simulation.View;
        List<Entity> stack = [];

        Picker.Pick(view, runs.ToArray(), map.CellCenter(x, y), Vector2.Zero, stack);

        Assert.NotEmpty(view.TileRuns.ToArray());
        Assert.Equal(hit ? [map] : [], stack);
    }
}

using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Tiles;

public sealed class TileAnimationTests
{
    // Frame 0 holds 2 steps and frame 1 holds 3, a loop of 5. Each frame is one 8px cell across the atlas.
    private static readonly TextureRegion First = new(8, 0, 8, 8);
    private static readonly TextureRegion Second = new(16, 0, 8, 8);
    private static readonly TextureRegion Rock = new(0, 0, 8, 8);

    // The clock is the map's own step count. Each frame holds for its ticks and the loop wraps to the
    // first frame. A cell painted mid-run joins the phase, and a pause stops the count where it stood.
    [Fact]
    public void EveryCellOfAnEntryDrawsTheFrameTheMapsStepsSelect_AndAPauseHoldsIt()
    {
        TileGrid grid = new(
            8,
            3,
            1,
            [TileGrid.EmptyTile, new TileType { Name = "falls", Frames = [new(1, 2), new(2, 3)] }, new TileType { Name = "rock", Cell = 0 }],
            [1, 1, 2],
            SceneFixtures.Atlas,
            4);
        TileMap map = new(grid);
        Scene scene = new();
        scene.Add(map);
        SceneFixtures.Open(scene, map.Size / 2f, map.Size);
        SceneSimulation simulation = new(scene);

        List<TextureRegion> first = [Regions(simulation)[0]];
        for (int step = 1; step <= 5; step++)
        {
            simulation.Step(SceneFixtures.Step());
            first.Add(Regions(simulation)[0]);
        }

        Assert.Equal([First, First, Second, Second, Second, First], first);
        Assert.Equal(Rock, Regions(simulation)[2]);

        map.SetTile(2, 0, "falls");
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([First, First, First], Regions(simulation));

        // Three held steps. Counting them would show the second frame now and the first after resuming.
        scene.Paused = true;
        for (int step = 0; step < 3; step++)
        {
            simulation.Step(SceneFixtures.Step());
        }

        Assert.Equal([First, First, First], Regions(simulation));

        scene.Paused = false;
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([Second, Second, Second], Regions(simulation));
    }

    private static TextureRegion[] Regions(SceneSimulation simulation) =>
        [.. simulation.View.Sprites.ToArray().OrderBy(static sprite => sprite.Position.X).Select(static sprite => sprite.Sprite.Region)];
}

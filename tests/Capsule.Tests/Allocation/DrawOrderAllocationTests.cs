using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class DrawOrderAllocationTests
{
    // Each step, one pair leaves and the other arrives higher band first. Arrivals out of order are
    // sorted before they merge into the order the frame keeps.
    [Fact]
    public void ArrivalsOutOfOrderEveryStep_PatchTheOrderAndAllocateNothing()
    {
        Entity[][] pairs = [[Holding(1), Holding(0)], [Holding(1), Holding(0)]];
        int steps = 0;
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, new Vector2(1000f, 1000f)));
        scene.Add(Holding(0));
        scene.Add(new SceneFixtures.Watcher(stepping =>
        {
            Entity[] arriving = pairs[steps % 2];
            Entity[] leaving = pairs[(steps + 1) % 2];
            if (steps++ > 0)
            {
                stepping.Remove(leaving[0]);
                stepping.Remove(leaving[1]);
            }

            stepping.Add(arriving[0]);
            stepping.Add(arriving[1]);
        }));
        using SimulationHost host = new(scene);
        _ = StepMeasurement.Bytes(host, measured: 0);
        int rebuilds = scene.Drawing.Rebuilds;

        long bytes = StepMeasurement.Bytes(host, warmup: 0);

        Assert.Equal(rebuilds, scene.Drawing.Rebuilds);
        Assert.Equal(0, bytes);
    }

    private static Entity Holding(int zIndex)
    {
        Entity entity = new EntityHierarchyFixtures.Node(Vector2.Zero);
        entity.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)) { ZIndex = zIndex });

        return entity;
    }
}

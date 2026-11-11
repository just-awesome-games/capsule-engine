using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class SceneDrawOrderBandTests
{
    // The renderer's change leaves the parent waiting to enter the order again. Its band then changes
    // while it waits, and its child holds a key the whole time.
    [Fact]
    public void AParentChangingItsBandWhileItWaitsToEnterTheOrder_MovesItsChildWithIt()
    {
        Node parent = Tagged(1);
        Node child = Tagged(2);
        child.Parent = parent;
        using SimulationHost run = Drawn(out _, parent, Tagged(3));

        parent.Get<SpriteRenderer>().ZIndex = 1;
        parent.ZIndex = 5;
        run.Step();

        Assert.Equal([3, 2, 1], Order(run.Simulation.View.Sprites));
    }

    // The child joins, leaves and joins again under a parent that joins after it. The order meets the
    // child first, before it has summed the parent's band.
    [Fact]
    public void AChildThatLeftAndCameBackUnderAnArrivingParent_DrawsInThatParentsBand()
    {
        Node high = Tagged(3);
        high.ZIndex = 2;
        using SimulationHost run = Drawn(out Scene scene, Tagged(1), high);

        Node child = Tagged(2);
        scene.Add(child);
        scene.Remove(child);
        Node parent = new(Vector2.Zero) { ZIndex = 5 };
        child.Parent = parent;
        scene.Add(parent);
        run.Step();

        Assert.Equal([1, 3, 2], Order(run.Simulation.View.Sprites));
    }

    // A scene holding the nodes that has stepped and built a frame, which a change then patches.
    private static SimulationHost Drawn(out Scene scene, params Node[] nodes)
    {
        scene = new SceneFixtures.HookScene();
        foreach (Node node in nodes)
        {
            scene.Add(node);
        }

        SimulationHost run = new(scene);
        run.Step();
        Assert.False(run.Simulation.View.Sprites.IsEmpty);

        return run;
    }

    private static Node Tagged(int tag)
    {
        Node node = new(Vector2.Zero);
        node.Add(Tag(tag));

        return node;
    }
}

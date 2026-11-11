using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class SceneYSortTests
{
    // Every entity stands at X zero and each renderer's offset is its tag. An intent's X names the
    // renderer that drew it.
    [Fact]
    public void RootsInABand_DrawByTheirYAsTheyMove_WithTheirSubtreeAndInsideTheirBand()
    {
        Node low = Root(10f, 1);

        // Its child stands far below it and still draws with its root.
        Node lowChild = new(new Vector2(0f, 100f));
        lowChild.Add(Tag(2));
        lowChild.Parent = low;

        Node middle = Root(20f, 3);
        Node tied = Root(20f, 4);

        // A child with a band of its own draws over the whole default band whatever its root's Y.
        Node raised = new(Vector2.Zero) { ZIndex = 5 };
        raised.Add(Tag(6));
        raised.Parent = middle;

        Node under = Root(1000f, 5);
        under.ZIndex = -1;

        // The screen layer keeps its walk order whatever its Y.
        Pinned lowOnScreen = new(50f, 7);
        Pinned highOnScreen = new(0f, 8);

        SceneFixtures.HookScene scene = new();
        foreach (Entity entity in new Entity[] { low, middle, tied, under, lowOnScreen, highOnScreen })
        {
            scene.Add(entity);
        }

        scene.YSort = true;

        using SimulationHost run = new(scene);
        run.Step();
        Assert.Equal([5, 1, 2, 3, 4, 6], Order(run.Simulation.View.Sprites));
        Assert.Equal([7, 8], Order(run.Simulation.View.ScreenSprites));

        low.Position = new Vector2(0f, 30f);
        tied.Position = new Vector2(0f, 0f);
        run.Step();
        Assert.Equal([5, 4, 3, 1, 2, 6], Order(run.Simulation.View.Sprites));

        // Equal Y falls back to walk order.
        tied.Position = new Vector2(0f, 20f);
        run.Step();
        Assert.Equal([5, 3, 4, 1, 2, 6], Order(run.Simulation.View.Sprites));

        scene.YSort = false;
        run.Step();
        Assert.Equal([5, 1, 2, 3, 4, 6], Order(run.Simulation.View.Sprites));
    }

    // The arrival lands between two roots by its Y while a held root moves past both, and a root that
    // leaves and returns is read where it then stands.
    [Fact]
    public void RootsJoiningAndLeavingAYSortedScene_DrawByTheirYWithoutARebuild()
    {
        Node low = Root(10f, 1);
        Node leaving = Root(20f, 2);
        Node high = Root(30f, 3);
        SceneFixtures.HookScene scene = new() { YSort = true };
        scene.Add(low);
        scene.Add(leaving);
        scene.Add(high);

        using SimulationHost run = new(scene);
        run.Step();
        Assert.Equal([1, 2, 3], Order(run.Simulation.View.Sprites));
        int rebuilds = scene.Drawing.Rebuilds;

        scene.Remove(leaving);
        Node arrival = Root(15f, 4);
        scene.Add(arrival);
        low.Position = new Vector2(0f, 40f);
        run.Step();
        Assert.Equal([4, 3, 1], Order(run.Simulation.View.Sprites));

        scene.Remove(arrival);
        scene.Add(arrival);
        arrival.Position = new Vector2(0f, 35f);
        run.Step();
        Assert.Equal([3, 4, 1], Order(run.Simulation.View.Sprites));
        Assert.Equal(rebuilds, scene.Drawing.Rebuilds);
    }

    // The root's keys stay in the order until it is next read, and its renderer no longer names it.
    [Fact]
    public void ARendererDetachedFromARootThatLeftAYSortedScene_IsGoneWhenTheTreeSpreadsItsLabels()
    {
        Node first = Root(10f, 1);
        Node gone = Root(20f, 2);
        Node last = Root(30f, 3);
        SceneFixtures.HookScene scene = new() { YSort = true };
        scene.Add(first);
        scene.Add(last);
        scene.Add(gone);
        using SimulationHost run = new(scene);
        run.Step();
        Assert.Equal([1, 2, 3], Order(run.Simulation.View.Sprites));

        scene.Remove(gone);
        gone.Remove(gone.Get<SpriteRenderer>());
        for (int index = 0; index < 70_000; index++)
        {
            _ = new Node(Vector2.Zero) { Parent = first };
        }

        run.Step();
        Assert.Equal([1, 3], Order(run.Simulation.View.Sprites));
    }

    private static Node Root(float y, int tag)
    {
        Node root = new(new Vector2(0f, y));
        root.Add(Tag(tag));

        return root;
    }

    private sealed class Pinned : ScreenEntity
    {
        internal Pinned(float y, int tag)
            : base(Anchor.TopLeft, new Vector2(0f, y)) => Add(Tag(tag));
    }
}

using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Scenes;

public sealed class SceneYSortTests
{
    // Every entity stands at X zero and each renderer's offset is its tag, so an intent's X names the
    // renderer that drew it.
    [Fact]
    public void RootsInABand_DrawByTheirYAsTheyMove_WithTheirSubtreeAndInsideTheirBand()
    {
        Marker low = Root(10f, 1);

        // Its child stands far below it and still draws with its root.
        Marker lowChild = new(new Vector2(0f, 100f));
        lowChild.Add(Tag(2));
        lowChild.Parent = low;

        Marker middle = Root(20f, 3);
        Marker tied = Root(20f, 4);

        // A child with a band of its own draws over the whole default band whatever its root's Y.
        Marker raised = new(Vector2.Zero) { ZIndex = 5 };
        raised.Add(Tag(6));
        raised.Parent = middle;

        Marker under = Root(1000f, 5);
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

    private static Marker Root(float y, int tag)
    {
        Marker root = new(new Vector2(0f, y));
        root.Add(Tag(tag));

        return root;
    }

    private static SpriteRenderer Tag(int tag) => new(SceneFixtures.Frame(1, 1)) { Offset = new Vector2(tag, 0f) };

    private static int[] Order(ReadOnlySpan<SpriteIntent> sprites)
    {
        int[] tags = new int[sprites.Length];
        for (int index = 0; index < tags.Length; index++)
        {
            tags[index] = (int)sprites[index].Position.X;
        }

        return tags;
    }

    private sealed class Marker(Vector2 position) : Entity(position);

    private sealed class Pinned : ScreenEntity
    {
        internal Pinned(float y, int tag)
            : base(Anchor.TopLeft, new Vector2(0f, y)) => Add(Tag(tag));
    }
}

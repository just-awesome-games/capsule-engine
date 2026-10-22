using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

// A variant is another look for a set of frames. The renderer holding a frame draws the variant's
// drawing of it, while the frame it holds still places every gameplay mark.
public sealed class SpriteVariantTests
{
    private const string Muzzle = "muzzle";

    // An 8x8 frame pivoted bottom-centre, with its muzzle on the right edge.
    private static readonly Sprite Held = SceneFixtures.Frame(0, new SpriteMarks([new(Muzzle, new Vector2(8f, 4f))]));

    private static readonly Sprite Other = SceneFixtures.Frame(1);

    // A hat drawn over the held frame, from a texture of its own.
    private static readonly Sprite Hat = new(new TextureHandle("hat", ".png"), new TextureRegion(0, 0, 6, 3), new Vector2(3f, 10f));

    [Fact]
    public void ARendererWithAVariant_DrawsTheMappedFrameWhileTheHeldFramePlacesItsSockets()
    {
        SpriteRenderer renderer = new(Held) { Variant = new SpriteVariant([Held], [Hat]) };
        EntityHierarchyFixtures.Node root = new(new Vector2(100f, 50f));
        root.Add(renderer);
        Entity muzzle = renderer.Socket(Muzzle);

        using SimulationHost run = Running(root);

        Assert.Equal(Hat, Assert.Single(run.Simulation.View.Sprites.ToArray()).Sprite);
        Assert.Equal(Held, renderer.Sprite);
        Assert.Equal(new Vector2(104f, 46f), muzzle.WorldPosition);
    }

    [Fact]
    public void AFrameTheVariantDoesNotMap_DrawsAsItself()
    {
        SpriteRenderer renderer = new(Other) { Variant = new SpriteVariant([Held], [Hat]) };
        EntityHierarchyFixtures.Node root = new(Vector2.Zero);
        root.Add(renderer);

        using SimulationHost run = Running(root);

        Assert.Equal(Other, Assert.Single(run.Simulation.View.Sprites.ToArray()).Sprite);
    }

    // An empty mapping is a frame the look has no drawing for, such as a hat hidden behind a turn.
    [Fact]
    public void AFrameMappedToNothing_DrawsNothingAndBoundsNothing()
    {
        SpriteRenderer renderer = new(Held) { Variant = new SpriteVariant([Held], [null]) };
        EntityHierarchyFixtures.Node root = new(Vector2.Zero);
        root.Add(renderer);

        using SimulationHost run = Running(root);

        Assert.Empty(run.Simulation.View.Sprites.ToArray());
        Assert.Equal(default, renderer.Bounds);
    }

    [Fact]
    public void CombiningVariantsThatMapOneFrame_Throws()
    {
        SpriteVariant first = new([Held], [Hat]);
        SpriteVariant second = new([Held, Other], [null, Hat]);

        Assert.Throws<ArgumentException>(() => SpriteVariant.Combine(first, second));
    }

    private static SimulationHost Running(Entity root)
    {
        SceneFixtures.HookScene scene = new();
        scene.Add(root);
        SimulationHost run = new(scene);
        run.Step();

        return run;
    }
}

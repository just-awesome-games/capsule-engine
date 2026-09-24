using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Rendering;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class SceneHidingTests
{
    public enum Hiding
    {
        HiddenAncestor,
        FadedAncestor,
        HiddenRenderer,
    }

    // Every way of drawing nothing is skipped before Draw, and none of them stops the entity stepping.
    [Theory]
    [InlineData(Hiding.HiddenAncestor)]
    [InlineData(Hiding.FadedAncestor)]
    [InlineData(Hiding.HiddenRenderer)]
    public void ARendererThatDrawsNothing_IsNotAskedToDraw_AndItsEntityStillSteps(Hiding hiding)
    {
        Entity parent = new Node(Vector2.Zero);
        SceneFixtures.Drifter child = new(Vector2.Zero);
        child.Parent = parent;
        CountingRenderer renderer = new();
        child.Add(renderer);

        switch (hiding)
        {
            case Hiding.HiddenAncestor:
                parent.Visible = false;
                break;
            case Hiding.FadedAncestor:
                parent.Tint = new ColorRgba(255, 255, 255, 0);
                break;
            case Hiding.HiddenRenderer:
                renderer.Visible = false;
                break;
        }

        SceneFixtures.HookScene scene = new();
        scene.Add(parent);
        using SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(0, renderer.Draws);
        Assert.Equal(0, simulation.View.Sprites.Length);
        Assert.Equal(new Vector2(1f, 0f), child.Position);
    }

    // The composed tint reaches every leaf once. A label's glyphs expand through the sprite leaf, so a
    // second multiply there would halve them again. A white tint takes no multiply at all.
    [Fact]
    public void ATintComposedDownTheTree_ReachesASpriteALabelAndALightExactlyOnce()
    {
        Entity parent = new Node(Vector2.Zero) { Tint = new ColorRgba(255, 128, 64) };
        Entity child = new(parent, Vector2.Zero) { Tint = new ColorRgba(128, 255, 255, 128) };
        child.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
        child.Add(new Label(FontFixtures.Font(), "A") { Color = new ColorRgba(200, 100, 255) });
        child.Add(new PointLight { Radius = 10f, Color = ColorRgba.Orange });

        ColorRgba odd = new(201, 37, 90, 180);
        Entity plain = new Node(Vector2.Zero);
        plain.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)) { Color = odd });

        SceneFixtures.HookScene scene = new();
        scene.Add(parent);
        scene.Add(plain);
        using SceneSimulation simulation = new(scene);

        SpriteIntent[] sprites = simulation.View.Sprites.ToArray();
        Assert.Equal(3, sprites.Length);
        Assert.Equal(new ColorRgba(128, 128, 64, 128), sprites[0].Color);
        Assert.Equal(new ColorRgba(100, 50, 64, 128), sprites[1].Color);
        Assert.Equal(odd, sprites[2].Color);
        Assert.Equal(new ColorRgba(128, 83, 0, 128), Assert.Single(simulation.View.Lights.ToArray()).Color);
    }

    // Two nested flashes are one mix with the parent's applied over the child's, composed in float and
    // rounded once where the intent is stored. A sprite under no flash is stored as its renderer made it.
    [Fact]
    public void AFlashComposedDownTheTree_ReachesTheStoredIntent_AndAnUnflashedOneIsUnchanged()
    {
        Entity parent = new Node(Vector2.Zero) { Flash = 0.5f, FlashColor = new ColorRgba(255, 0, 0) };
        Entity child = new(parent, Vector2.Zero) { Flash = 0.5f, FlashColor = new ColorRgba(0, 0, 255, 7) };
        child.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));

        ColorRgba odd = new(201, 37, 90, 180);
        Entity plain = new Node(Vector2.Zero);
        SpriteRenderer unflashed = new(SceneFixtures.Frame(4, 4)) { Color = odd };
        plain.Add(unflashed);

        SceneFixtures.HookScene scene = new();
        scene.Add(parent);
        scene.Add(plain);
        using SceneSimulation simulation = new(scene);

        SpriteIntent[] sprites = simulation.View.Sprites.ToArray();
        Assert.Equal(2, sprites.Length);

        // 1 - (1 - 0.5)(1 - 0.5) = 0.75 mixed, two thirds of it the parent's red. The parent's half is
        // stored as 128, which rounds the amount up to 192.
        Assert.Equal(new ColorRgba(170, 0, 85, 192), sprites[0].Flash);
        Assert.Equal(ColorRgba.White, sprites[0].Color);
        Assert.Equal(new SpriteIntent(SceneFixtures.Frame(4, 4), Vector2.Zero, Vector2.Zero, 0f, 0f, new Vector2(4f, 4f), false, false, odd), sprites[1]);
    }

    // The composed state is cached like the world transform. A parent change must re-stale it, or a
    // child read once under no parent keeps drawing as it did there.
    [Fact]
    public void AnEntityReparentedAfterItsStateWasRead_TakesItsNewParentsTintAndVisibility()
    {
        Entity child = new Node(Vector2.Zero);
        Assert.True(child.TryGetDrawStyle(out ColorRgba before, out _));
        Assert.Equal(ColorRgba.White, before);

        ColorRgba shade = new(10, 20, 30, 40);
        Entity tinted = new Node(Vector2.Zero) { Tint = shade };
        child.Parent = tinted;

        Assert.True(child.TryGetDrawStyle(out ColorRgba under, out _));
        Assert.Equal(shade, under);

        Entity hidden = new Node(Vector2.Zero) { Visible = false };
        child.Parent = hidden;

        Assert.False(child.ShownInTree);

        child.Parent = null;

        Assert.True(child.ShownInTree);
    }

    private sealed class CountingRenderer : Renderer
    {
        internal int Draws { get; private set; }

        protected internal override void Draw(FrameView view)
        {
            Draws++;
            view.Add(new SpriteIntent(SceneFixtures.Frame(1, 1), Vector2.Zero, Vector2.Zero, 0f, 0f, Vector2.One, false, false, ColorRgba.White));
        }
    }
}

using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Rendering;

// A ClipRect cuts a screen subtree to its entity's rect. The cut is per renderer state, as the material
// is. Draw bands can interleave the subtree with renderers from outside it.
public sealed class ClipRectTests
{
    // A frame at (10, 10) spanning 40 by 20.
    private static readonly Rect Frame = new(10f, 10f, 50f, 30f);

    [Fact]
    public void ClipRuns_OpenOnlyWhereTheClipChanges_SoABandedRendererFromOutsideTheSubtreeIsNotClipped()
    {
        ScreenEntity frame = Clipped(new Vector2(10f, 10f), new Vector2(40f, 20f));
        Fill(frame, new Vector2(2f, 2f), zIndex: 0);
        Fill(frame, new Vector2(8f, 2f), zIndex: 2);
        ScreenEntity outsider = new(Anchor.TopLeft, new Vector2(100f, 100f)) { Size = new Vector2(8f, 8f), ZIndex = 1 };
        outsider.Add(new ColorRect());

        using SceneSimulation simulation = Simulate(frame, outsider);

        ClipRegion region = new(Frame, Frame);
        Assert.Equal(3, simulation.View.ScreenSprites.Length);
        Assert.Equal([(0, 0, 1), (1, 0, 0), (2, 0, 1)], simulation.View.ScreenClipRuns.ToArray().Select(run => (run.FirstSprite, run.FirstLine, run.Levels)));
        Assert.Equal([region, region], simulation.View.ScreenClipLevels.ToArray());
    }

    // A run is told apart by where it starts in both lists. The lines draw after every sprite.
    [Fact]
    public void AClipRun_RecordsItsFirstSpriteAndItsFirstLine_AndALayerWithNoCanvasStillCullsToTheClip()
    {
        FrameView view = new();
        Rect clip = new(0f, 0f, 10f, 10f);
        LineIntent line = new(Vector2.Zero, new Vector2(5f, 5f), ColorRgba.White);

        view.BeginClip();
        view.AddClipLevel(clip, clip);
        view.EndClip();
        view.Add(line, RenderSpace.Screen);
        view.Add(Quad(new Vector2(2f, 2f)), RenderSpace.Screen);
        view.Add(Quad(new Vector2(500f, 500f)), RenderSpace.Screen);
        view.ClearClip();
        view.Add(line, RenderSpace.Screen);
        view.Add(Quad(new Vector2(500f, 500f)), RenderSpace.Screen);

        Assert.Equal([(0, 0, 1), (1, 1, 0)], view.ScreenClipRuns.ToArray().Select(run => (run.FirstSprite, run.FirstLine, run.Levels)));
        Assert.Equal([new ClipRegion(clip, clip)], view.ScreenClipLevels.ToArray());
        Assert.Equal(2, view.ScreenSprites.Length);

        view.Clear();
        view.Add(Quad(new Vector2(500f, 500f)), RenderSpace.Screen);

        Assert.Single(view.ScreenSprites.ToArray());
        Assert.True(view.ScreenClipRuns.IsEmpty);
        Assert.True(view.ScreenClipLevels.IsEmpty);
    }

    [Fact]
    public void NestedClips_CarryOneLevelEachAndShowOnlyWhatAllShow()
    {
        ScreenEntity outer = Clipped(new Vector2(10f, 10f), new Vector2(100f, 60f));
        ScreenEntity inner = Clipped(new Vector2(60f, 30f), new Vector2(100f, 60f));
        inner.Parent = outer;
        inner.Add(new ColorRect());

        using SceneSimulation simulation = Simulate(outer);

        // The inner rect is (70, 40) to (170, 100) and the outer is (10, 10) to (110, 70). The run keeps each
        // level whole for the renderer to interpolate, and the pointer reads their intersection.
        Rect inside = new(70f, 40f, 170f, 100f);
        Rect around = new(10f, 10f, 110f, 70f);
        Assert.Equal([new ClipRun(0, 0, 0, 2)], simulation.View.ScreenClipRuns.ToArray());
        Assert.Equal([new ClipRegion(inside, inside), new ClipRegion(around, around)], simulation.View.ScreenClipLevels.ToArray());
        Assert.True(ClipRect.Admits(inner, new Vector2(100f, 50f)));
        Assert.False(ClipRect.Admits(inner, new Vector2(120f, 50f)));
        Assert.False(ClipRect.Admits(inner, new Vector2(100f, 80f)));
    }

    [Fact]
    public void TheRegion_CarriesBothEndsOfTheStepForTheRendererToInterpolate()
    {
        ScreenEntity frame = Clipped(new Vector2(10f, 10f), new Vector2(40f, 20f));
        Fill(frame, Vector2.Zero, zIndex: 0);

        using SceneSimulation simulation = Slide(frame, frame, new Vector2(14f, 10f));

        Assert.Equal([new ClipRegion(Frame, new Rect(14f, 10f, 54f, 30f))], simulation.View.ScreenClipLevels.ToArray());
    }

    // The ends of a moving inner clip intersect the outer one in nothing at all, though halfway through the
    // step the inner clip lies wholly over it. Interpolating the intersection would show nothing, and so
    // would a cull against it.
    [Fact]
    public void AMovingInnerClip_IsKeptWholeSoTheStepIsNotOverClippedOrCulledAway()
    {
        ScreenEntity outer = Clipped(Vector2.Zero, new Vector2(10f, 10f));
        ScreenEntity inner = Clipped(new Vector2(-15f, 0f), new Vector2(10f, 10f));
        inner.Parent = outer;
        inner.Add(new ColorRect());

        using SceneSimulation simulation = Slide(outer, inner, new Vector2(15f, 0f));

        Rect outerRect = new(0f, 0f, 10f, 10f);
        Assert.Equal(
            [new ClipRegion(new Rect(-15f, 0f, -5f, 10f), new Rect(15f, 0f, 25f, 10f)), new ClipRegion(outerRect, outerRect)],
            simulation.View.ScreenClipLevels.ToArray());
        Assert.Single(simulation.View.ScreenSprites.ToArray());
    }

    // A sprite or a label wholly outside the region is never stored, and one that only overlaps it is kept
    // for the scissor to trim.
    [Fact]
    public void ASpriteAndALabelWhollyOutsideTheClip_AreCulled()
    {
        ScreenEntity frame = Clipped(new Vector2(10f, 10f), new Vector2(40f, 20f));
        Fill(frame, new Vector2(2f, 2f), zIndex: 0);
        Fill(frame, new Vector2(36f, 2f), zIndex: 0, new Vector2(20f, 4f));
        Fill(frame, new Vector2(100f, 0f), zIndex: 0);
        ScreenEntity row = new(Anchor.TopLeft, new Vector2(0f, 60f)) { Parent = frame, Size = new Vector2(20f, 10f) };
        row.Add(new Label(FontFixtures.Font(), "AB"));

        using SceneSimulation simulation = Simulate(frame);

        Assert.Equal([new Vector2(12f, 12f), new Vector2(46f, 12f)], simulation.View.ScreenSprites.ToArray().Select(sprite => sprite.Position));
    }

    private static SceneSimulation Simulate(params ReadOnlySpan<ScreenEntity> roots)
    {
        Scene scene = new();
        foreach (ScreenEntity root in roots)
        {
            scene.Add(root);
        }

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        return simulation;
    }

    // Steps once in place, then once more with `mover` moved to `to`. Its previous end is where it began.
    private static SceneSimulation Slide(ScreenEntity root, ScreenEntity mover, Vector2 to)
    {
        SceneFixtures.HookScene scene = new(step: (Scene _, in StepContext context) =>
        {
            if (context.Tick == 1)
            {
                mover.Position = to;
            }
        });
        scene.Add(root);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step(1));

        return simulation;
    }

    private static ScreenEntity Clipped(Vector2 offset, Vector2 size)
    {
        ScreenEntity frame = new(Anchor.TopLeft, offset) { Size = size };
        frame.Add(new ClipRect());

        return frame;
    }

    // A rect of flat colour under `frame`, 4 by 4 unless given a size.
    private static void Fill(ScreenEntity frame, Vector2 offset, int zIndex, Vector2 size = default)
    {
        ScreenEntity child = new(Anchor.TopLeft, offset) { Parent = frame, Size = size == default ? new Vector2(4f, 4f) : size, ZIndex = zIndex };
        child.Add(new ColorRect());
    }

    private static SpriteIntent Quad(Vector2 position) =>
        new(Sprite.White, position, position, PreviousRotation: 0f, Rotation: 0f, new Vector2(4f, 4f), FlipX: false, FlipY: false, ColorRgba.White);
}

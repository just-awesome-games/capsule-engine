using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// A frame carries two ordered lists: the world the camera culls, and the screen layer the canvas
// culls, which draws over the whole of the world whatever either one holds.
public sealed class ScreenLayerTests
{
    [Fact]
    public void ScreenIntent_LandsOnItsOwnListInTheOrderAdded()
    {
        FrameView view = Canvas(100f, 100f);

        view.Add(Quad(new Vector2(1f, 1f)), RenderSpace.World);
        view.Add(Quad(new Vector2(2f, 2f)), RenderSpace.Screen);
        view.Add(Quad(new Vector2(3f, 3f)), RenderSpace.Screen);

        Assert.Equal(new Vector2(1f, 1f), Assert.Single(view.Sprites.ToArray()).Position);
        Assert.Equal([new Vector2(2f, 2f), new Vector2(3f, 3f)], view.ScreenSprites.ToArray().Select(sprite => sprite.Position));
        Assert.Equal(new RenderMetrics(Submitted: 3, Visible: 3), view.Metrics);
    }

    [Fact]
    public void TheSpaceTheFrameIsSetTo_IsWhereAnUnqualifiedAddLands()
    {
        FrameView view = Canvas(100f, 100f);
        view.Space = RenderSpace.Screen;

        view.Add(Quad(Vector2.Zero));
        view.Add(Text());

        // The rect, then one sprite per glyph of the run.
        Assert.Empty(view.Sprites.ToArray());
        Assert.Equal(4, view.ScreenSprites.Length);
    }

    [Fact]
    public void ScreenIntent_IsCulledAgainstTheCanvasRatherThanTheCamera()
    {
        // A camera looking at nothing near the canvas: what the screen layer is culled against is the
        // canvas alone.
        FrameView view = Canvas(10f, 10f);
        view.Camera = new CameraView(new Vector2(500f, 500f), new Vector2(4f, 4f));

        view.Add(Quad(new Vector2(1f, 1f)), RenderSpace.Screen);
        view.Add(Quad(new Vector2(40f, 1f)), RenderSpace.Screen);
        view.Add(Quad(new Vector2(-8f, 1f)), RenderSpace.Screen);

        Assert.Equal(new Vector2(1f, 1f), Assert.Single(view.ScreenSprites.ToArray()).Position);
        Assert.Equal(new RenderMetrics(Submitted: 3, Visible: 1), view.Metrics);
    }

    // A screen root sorted with the world stores on the world's list under a canvas run, culled against the
    // canvas and its clip rather than the camera. The world intent after it is placed by the world again and
    // is cut by no clip, though the clip it left behind still stands for the next screen renderer.
    [Fact]
    public void ScreenIntentSortedWithTheWorld_LandsOnTheWorldListInCanvasPixels_AndOnlyItIsClipped()
    {
        FrameView view = Canvas(100f, 100f);
        view.Camera = new CameraView(new Vector2(500f, 500f), new Vector2(40f, 40f));
        Rect clip = Rect.FromEdges(0f, 0f, 50f, 50f);

        view.Add(Quad(new Vector2(500f, 500f)), RenderSpace.World);
        view.Space = RenderSpace.Screen;
        view.SortsWithWorld = true;
        view.BeginClip();
        view.AddClipLevel(clip, clip);
        view.EndClip();
        view.Add(Quad(new Vector2(2f, 2f)));
        view.Add(Quad(new Vector2(60f, 60f)));
        view.Space = RenderSpace.World;
        view.SortsWithWorld = false;
        view.Add(Quad(new Vector2(501f, 501f)));

        Assert.Empty(view.ScreenSprites.ToArray());
        Assert.Equal([new Vector2(500f, 500f), new Vector2(2f, 2f), new Vector2(501f, 501f)], view.Sprites.ToArray().Select(sprite => sprite.Position));
        Assert.Equal([new PlacementRun(1, 0, Vector2.One, Canvas: true), new PlacementRun(2, 0, Vector2.One)], view.PlacementRuns.ToArray());
        Assert.Equal([new ClipRun(1, 0, 0, 1), new ClipRun(2, 0, 0, 0)], view.ClipRuns.ToArray());
    }

    [Fact]
    public void ACulledScreenSprite_IsOneThatSweepsClearOfTheCanvasAcrossTheWholeStep()
    {
        FrameView view = Canvas(10f, 10f);

        // Off the canvas at both ends, but across it in between.
        view.Add(Quad(new Vector2(-20f, 1f)) with { Position = new Vector2(30f, 1f) }, RenderSpace.Screen);

        Assert.Single(view.ScreenSprites.ToArray());
    }

    [Fact]
    public void ANonPositiveCanvas_CullsNothing()
    {
        FrameView view = new();

        view.Add(Quad(new Vector2(-500f, -500f)), RenderSpace.Screen);

        Assert.Single(view.ScreenSprites.ToArray());
    }

    [Fact]
    public void ARewrittenFrame_DropsBothListsAndReturnsToWorldSpace()
    {
        FrameView view = Canvas(100f, 100f);
        view.Space = RenderSpace.Screen;
        view.Add(Quad(Vector2.Zero));

        view.Clear();

        Assert.Empty(view.ScreenSprites.ToArray());
        Assert.Equal(RenderSpace.World, view.Space);
        Assert.Equal(new RenderMetrics(Submitted: 0, Visible: 0), view.Metrics);
    }

    private static FrameView Canvas(float width, float height) => new() { Canvas = new Vector2(width, height) };

    private static SpriteIntent Quad(Vector2 position) =>
        new(
            Sprite.White,
            position,
            position,
            PreviousRotation: 0f,
            Rotation: 0f,
            new Vector2(4f, 4f),
            FlipX: false,
            FlipY: false,
            ColorRgba.White);

    private static TextIntent Text() =>
        new(FontFixtures.Font(), "AB\nA", Vector2.Zero, Vector2.Zero, Vector2.One, ColorRgba.White);
}

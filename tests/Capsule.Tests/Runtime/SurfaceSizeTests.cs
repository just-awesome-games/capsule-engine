using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Runtime;

// The size of the surface the world draws to, from the declared resolution, the camera's fit and the window.
public sealed class SurfaceSizeTests
{
    // A declared span that matches a 320x180 canvas one world unit to the pixel.
    private static readonly Vector2 Canvas = new(320f, 180f);

    [Theory]
    [InlineData(3440, 1440)]
    [InlineData(1280, 720)]
    [InlineData(640, 1200)]
    public void SurfaceSize_UnderLetterbox_IsTheDeclaredCanvasWhateverShapeTheWindowIs(int windowWidth, int windowHeight)
    {
        CameraView camera = new(Vector2.Zero, Canvas);
        Vector2 window = new(windowWidth, windowHeight);

        Assert.Equal(
            (320, 180),
            FrameLayout.SurfaceSize((320, 180), camera, camera.ResolveSpan(window), windowWidth, windowHeight));
    }

    // Subtracting the resolved rect's edges reconstructs 320.001953125 this far out, whose scale
    // quantises sprites onto a grid a pixel off the canvas's own. The span is carried, not derived.
    [Fact]
    public void SurfaceSize_UnderLetterbox_IsTheCanvasWhereEdgeSubtractionWouldLosePrecision()
    {
        CameraView camera = new(new Vector2(32767.1f, 0f), Canvas);
        Vector2 window = new(1280f, 720f);
        Rect world = camera.Resolve(1f, window);

        Assert.NotEqual(Canvas.X, world.Right - world.Left);
        Assert.Equal(Canvas, camera.ResolveSpan(window));

        Vector2 span = camera.ResolveSpan(window);

        Assert.Equal((320, 180), FrameLayout.SurfaceSize((320, 180), camera, span, 1280, 720));
        Assert.Equal(4f, Letterbox.Fit(span.X, span.Y, 1280, 720).Scale);
        Assert.NotEqual(4f, Letterbox.Fit(world.Right - world.Left, world.Bottom - world.Top, 1280, 720).Scale);
    }

    // The canvas gives one surface pixel per world unit here. The expanded surface is the
    // expanded span: the extra width is world, not a coarser pixel.
    [Fact]
    public void SurfaceSize_UnderExpand_GrowsTheSlackAxisAtTheCanvasScale()
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, Canvas, ViewportFit.Expand);

        Assert.Equal(
            (430, 180),
            FrameLayout.SurfaceSize((320, 180), camera, camera.ResolveSpan(new Vector2(3440, 1440)), 3440, 1440));
    }

    [Fact]
    public void SurfaceSize_NeverExceedsTheWindowNorFallsBelowTheCanvas()
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, Canvas, ViewportFit.Expand);

        // A 100:1 window expands the span to 18000 world units, which the window can never show
        // more than 1200 pixels of. The 12-pixel height still gets the whole canvas.
        Assert.Equal(
            (1200, 180),
            FrameLayout.SurfaceSize((320, 180), camera, camera.ResolveSpan(new Vector2(1200, 12)), 1200, 12));
    }
}

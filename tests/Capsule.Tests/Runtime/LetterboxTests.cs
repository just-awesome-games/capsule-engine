using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

public sealed class LetterboxTests
{
    // A declared span that matches a 320x180 canvas one world unit to the pixel.
    private static readonly Vector2 Canvas = new(320f, 180f);

    [Theory]
    [InlineData(320f, 180f, 1920, 1080)]
    [InlineData(320f, 180f, 1000, 1000)]
    [InlineData(4f, 3f, 3440, 1440)]
    [InlineData(16f, 9f, 640, 480)]
    public void Fit_ScalesBothAxesByTheSameFactor(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.Equal(contentWidth * fit.Scale, fit.Width, 0.5);
        Assert.Equal(contentHeight * fit.Scale, fit.Height, 0.5);
    }

    [Fact]
    public void Fit_PillarboxesAContainerWiderThanTheContent()
    {
        Letterbox fit = Letterbox.Fit(320f, 180f, 3440, 1440);

        Assert.Equal(new Letterbox(440, 0, 2560, 1440, 8f), fit);
    }

    [Fact]
    public void Fit_LetterboxesAContainerTallerThanTheContent()
    {
        Letterbox fit = Letterbox.Fit(320f, 180f, 1920, 1480);

        Assert.Equal(new Letterbox(0, 200, 1920, 1080, 6f), fit);
    }

    [Theory]
    [InlineData(320f, 180f, 1000, 1000)]
    [InlineData(320f, 180f, 1001, 777)]
    [InlineData(1f, 1f, 1920, 1081)]
    public void Fit_LeavesEqualBarsOnEitherSide(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.InRange(containerWidth - fit.Width - (2 * fit.X), 0, 1);
        Assert.InRange(containerHeight - fit.Height - (2 * fit.Y), 0, 1);
    }

    [Theory]
    [InlineData(320f, 180f, 1920, 1080)]
    [InlineData(320f, 180f, 800, 450)]
    public void Fit_FillsTheContainerExactlyWhenTheAspectsMatch(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.Equal(new Letterbox(0, 0, containerWidth, containerHeight, containerWidth / contentWidth), fit);
    }

    [Theory]
    [InlineData(320f, 180f, 0, 1080)]
    [InlineData(320f, 180f, 1920, 0)]
    [InlineData(320f, 180f, -1920, -1080)]
    [InlineData(0f, 0f, 1920, 1080)]
    [InlineData(float.NaN, 180f, 1920, 1080)]
    [InlineData(320f, float.NaN, 1920, 1080)]
    public void Fit_IsEmptyForDegenerateGeometry(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Assert.True(Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight).IsEmpty);
    }

    [Fact]
    public void Fit_IsEmptyWhenTheFittedContentRoundsToNothing()
    {
        Assert.True(Letterbox.Fit(1000f, 1f, 10, 10).IsEmpty);
    }

    [Theory]
    [InlineData(320, 180, 1280, 720, 4f)]
    [InlineData(320, 180, 1279, 719, 3f)]
    [InlineData(320, 180, 1366, 768, 4f)]
    [InlineData(320, 180, 3440, 1440, 8f)]
    [InlineData(320, 180, 640, 359, 1f)]
    public void FitPixels_TakesTheLargestWholeScaleThatFits(
        int contentWidth,
        int contentHeight,
        int containerWidth,
        int containerHeight,
        float expectedScale)
    {
        Letterbox fit = Letterbox.FitPixels(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.Equal(expectedScale, fit.Scale);
        Assert.Equal((int)(contentWidth * expectedScale), fit.Width);
        Assert.Equal((int)(contentHeight * expectedScale), fit.Height);
    }

    [Theory]
    [InlineData(320, 180, 1366, 768)]
    [InlineData(320, 180, 1001, 777)]
    [InlineData(320, 180, 100, 100)]
    public void FitPixels_LeavesEqualBarsOnEitherSide(int contentWidth, int contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.FitPixels(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.InRange(containerWidth - fit.Width - (2 * fit.X), 0, 1);
        Assert.InRange(containerHeight - fit.Height - (2 * fit.Y), 0, 1);
    }

    [Theory]
    [InlineData(320, 180, 160, 90)]
    [InlineData(320, 180, 319, 179)]
    public void FitPixels_FallsBackToTheFractionalFitBelowOneWholeScale(
        int contentWidth,
        int contentHeight,
        int containerWidth,
        int containerHeight)
    {
        Letterbox fit = Letterbox.FitPixels(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.Equal(Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight), fit);
        Assert.InRange(fit.Scale, 0f, 1f);
    }

    [Theory]
    [InlineData(320, 180, 0, 720)]
    [InlineData(320, 180, 1280, 0)]
    [InlineData(0, 180, 1280, 720)]
    [InlineData(320, 0, 1280, 720)]
    [InlineData(320, 180, -1280, -720)]
    public void FitPixels_IsEmptyForDegenerateGeometry(int contentWidth, int contentHeight, int containerWidth, int containerHeight)
    {
        Assert.True(Letterbox.FitPixels(contentWidth, contentHeight, containerWidth, containerHeight).IsEmpty);
    }

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
            FrameRenderer.SurfaceSize((320, 180), Canvas, camera.ResolveSpan(window), windowWidth, windowHeight));
    }

    // Subtracting the resolved rect's edges reconstructs 320.001953125 this far out, whose scale
    // quantises sprites onto a grid a pixel off the canvas's own. The span is carried, not derived.
    [Fact]
    public void SurfaceSize_UnderLetterbox_IsTheCanvasWhereEdgeSubtractionWouldLosePrecision()
    {
        CameraView camera = new(new Vector2(32767.1f, 0f), Canvas);
        Vector2 window = new(1280f, 720f);
        ViewBounds world = camera.Resolve(1f, window);

        Assert.NotEqual(Canvas.X, world.Right - world.Left);
        Assert.Equal(Canvas, camera.ResolveSpan(window));

        Vector2 span = camera.ResolveSpan(window);

        Assert.Equal((320, 180), FrameRenderer.SurfaceSize((320, 180), Canvas, span, 1280, 720));
        Assert.Equal(4f, Letterbox.Fit(span.X, span.Y, 1280, 720).Scale);
        Assert.NotEqual(4f, Letterbox.Fit(world.Right - world.Left, world.Bottom - world.Top, 1280, 720).Scale);
    }

    // The canvas gives one surface pixel per world unit here, so the expanded surface is the
    // expanded span: the extra width is world, not a coarser pixel.
    [Fact]
    public void SurfaceSize_UnderExpand_GrowsTheSlackAxisAtTheCanvasScale()
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, Canvas, ViewportFit.Expand);

        Assert.Equal(
            (430, 180),
            FrameRenderer.SurfaceSize((320, 180), Canvas, camera.ResolveSpan(new Vector2(3440, 1440)), 3440, 1440));
    }

    [Fact]
    public void SurfaceSize_NeverExceedsTheWindowNorFallsBelowTheCanvas()
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, Canvas, ViewportFit.Expand);

        // A 100:1 window expands the span to 18000 world units, which the window can never show
        // more than 1200 pixels of; the 12-pixel height still gets the whole canvas.
        Assert.Equal(
            (1200, 180),
            FrameRenderer.SurfaceSize((320, 180), Canvas, camera.ResolveSpan(new Vector2(1200, 12)), 1200, 12));
    }

    [Fact]
    public void PresentFit_TakesTheWholeScaleForPointAndTheFractionalOneForLinear()
    {
        Assert.Equal(
            Letterbox.FitPixels(320, 180, 1366, 768),
            FrameRenderer.PresentFit(TextureSampling.Point, 320, 180, 1366, 768));
        Assert.Equal(
            Letterbox.Fit(320, 180, 1366, 768),
            FrameRenderer.PresentFit(TextureSampling.Linear, 320, 180, 1366, 768));
    }
}

using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

// Where the screen layer lands in the window, and the inverse of that placement, which is what turns a
// sampled mouse position into the canvas position the simulation reads.
public sealed class ScreenPlacementTests
{
    private static readonly Vector2 Canvas = new(320f, 180f);

    [Fact]
    public void WithNoRenderSurface_TheCanvasTakesItsOwnCentredFitOfTheWindow()
    {
        // Twice the canvas on X and three times on Y: the binding axis is X, and the bars are on Y.
        ScreenPlacement placement = FrameLayout.WindowPlacement(Canvas, 640, 540);

        Assert.Equal(2f, placement.Scale);
        Assert.Equal(new Vector2(0f, 90f), placement.Origin);
        Assert.Equal(Vector2.Zero, placement.ToCanvas(new Vector2(0f, 90f)));
        Assert.Equal(new Vector2(160f, 90f), placement.ToCanvas(new Vector2(320f, 270f)));
    }

    [Fact]
    public void AWindowWithNoArea_PlacesNothing()
    {
        Assert.Equal(0f, FrameLayout.WindowPlacement(Canvas, 0, 540).Scale);
        Assert.Equal(0f, FrameLayout.WindowPlacement(Vector2.Zero, 640, 540).Scale);
    }

    // Point sampling owes its source pixels a square block each, so it takes the whole scale and
    // lets the bars absorb the remainder, the odd pixel going below the surface; linear sampling
    // answers to no pixel grid and fills the window it fits. 700 by 400 holds 320 by 180 twice over
    // and 1904 by 1041 five times, leaving bars of 304 and 141 pixels.
    [Theory]
    [InlineData(TextureSampling.Point, 700, 400, 2f, 30f, 20f)]
    [InlineData(TextureSampling.Point, 1904, 1041, 5f, 152f, 70f)]
    [InlineData(TextureSampling.Linear, 700, 400, 700f / 320f, 0f, 3f)]
    public void TheRenderSurface_IsPlacedOnTheFitItsSamplingCallsFor(
        TextureSampling sampling,
        int windowWidth,
        int windowHeight,
        float scale,
        float originX,
        float originY)
    {
        ScreenPlacement placement = FrameLayout.TargetPlacement(sampling, 320, 180, windowWidth, windowHeight);

        Assert.Equal(scale, placement.Scale);
        Assert.Equal(new Vector2(originX, originY), placement.Origin);
    }

    // A bar of an odd number of pixels has no whole-pixel centre, and a present origin on a half pixel
    // puts every texel boundary of a point-sampled surface on a pixel centre.
    [Theory]
    [InlineData(TextureSampling.Point)]
    [InlineData(TextureSampling.Linear)]
    public void TheRenderSurface_IsPresentedOnWholePixelsWhateverTheBarsArePlacedOn(TextureSampling sampling)
    {
        // 1904 by 1041 holds 320 by 180 five times over, leaving bars of 304 and 141 pixels.
        ScreenPlacement placement = FrameLayout.TargetPlacement(sampling, 320, 180, 1904, 1041);

        Assert.Equal(MathF.Truncate(placement.Origin.X), placement.Origin.X);
        Assert.Equal(MathF.Truncate(placement.Origin.Y), placement.Origin.Y);
    }

    [Fact]
    public void OnASurfaceNoLargerThanTheCanvas_TheLayerSitsAtItsCorner()
    {
        Assert.Equal(Vector2.Zero, FrameLayout.ScreenSlack(320, 180, Canvas));
    }

    [Fact]
    public void OnASurfaceTheCameraGrew_TheLayerStaysTheCanvasCentredInIt()
    {
        // Expand and FixedHeight draw a wider surface to reveal more world; the layer is the canvas,
        // centred on whole pixels so it lands on the grid the world already used.
        Assert.Equal(new Vector2(40f, 0f), FrameLayout.ScreenSlack(400, 180, Canvas));
        Assert.Equal(new Vector2(0f, 10f), FrameLayout.ScreenSlack(320, 201, Canvas));
    }

    [Fact]
    public void ASampledMouse_ReadsACanvasPositionThroughTheWholeTwoStepPlacement()
    {
        // The layer sits at the slack inside the surface, and the surface is presented at its own
        // placement: a window pixel unwinds both at once.
        Vector2 slack = FrameLayout.ScreenSlack(400, 180, Canvas);
        ScreenPlacement presented = FrameLayout.TargetPlacement(TextureSampling.Point, 400, 180, 800, 360);
        ScreenPlacement layer = new(presented.Origin + (slack * presented.Scale), presented.Scale);

        Assert.Equal(2f, layer.Scale);
        Assert.Equal(new Vector2(80f, 0f), layer.Origin);
        Assert.Equal(Vector2.Zero, layer.ToCanvas(new Vector2(80f, 0f)));
        Assert.Equal(new Vector2(320f, 180f), layer.ToCanvas(new Vector2(720f, 360f)));
    }

    [Fact]
    public void AMouseOffTheLayer_ReadsOffTheCanvas()
    {
        ScreenPlacement placement = FrameLayout.WindowPlacement(Canvas, 640, 540);

        // In the bar above the layer, and past its right edge.
        Assert.Equal(new Vector2(0f, -45f), placement.ToCanvas(Vector2.Zero));
        Assert.Equal(320.5f, placement.ToCanvas(new Vector2(641f, 90f)).X);
    }

    // The host samples the mouse before the first frame draws, so the placement is settled from the
    // frame view and the back buffer up front: window pixels are never handed over as canvas pixels.
    [Fact]
    public void BeforeAFrameHasDrawn_AWindowPixelAlreadyReadsThroughTheLayersPlacement()
    {
        ScreenPlacement declared = FrameLayout.Layout((320, 180), View(Canvas), 1280, 720).Layer;

        Assert.Equal(4f, declared.Scale);
        Assert.Equal(new Vector2(40f, 25f), declared.ToCanvas(new Vector2(160f, 100f)));

        // With no declared resolution the canvas is the configured window, which fills it at scale 1.
        Vector2 window = new(1280f, 720f);
        ScreenPlacement windowed = FrameLayout.Layout(null, View(window, window), 1280, 720).Layer;

        Assert.Equal(1f, windowed.Scale);
        Assert.Equal(new Vector2(160f, 100f), windowed.ToCanvas(new Vector2(160f, 100f)));
    }

    [Fact]
    public void BeforeAFrameHasDrawn_AWindowWithNoAreaPlacesNothing()
    {
        Assert.Equal(0f, FrameLayout.Layout((320, 180), View(Canvas), 0, 720).Layer.Scale);
        Assert.Equal(0f, FrameLayout.Layout(null, View(Canvas), 0, 720).Layer.Scale);
    }

    // One routine resolves the surface, the slack in it and the present, so the layer a pointer is
    // sampled through before the first frame is the layer that frame draws at — including under a fit
    // that grows the surface past the declared resolution on a window of another aspect.
    [Theory]
    [InlineData(ViewportFit.Letterbox, 320, 180, 3f, 0f, 1f)]
    [InlineData(ViewportFit.Expand, 320, 180, 3f, 0f, 1f)]
    [InlineData(ViewportFit.FixedHeight, 320, 180, 3f, 0f, 1f)]
    public void TheLayer_FollowsTheSurfaceTheCamerasFitAsksFor(
        ViewportFit fit,
        int surfaceWidth,
        int surfaceHeight,
        float scale,
        float originX,
        float originY)
    {
        // 960 by 542 is a hair narrower than the canvas: the two thirds of a row Expand would reveal
        // round down to none, so the surface stays the canvas and the present keeps its whole scale
        // with a one-pixel bar, rather than growing by a row that would cost the present a scale.
        ScreenLayout layout = FrameLayout.Layout((320, 180), View(Canvas, Canvas, fit), 960, 542);

        Assert.Equal((surfaceWidth, surfaceHeight), layout.Surface);
        Assert.Equal(scale, layout.Layer.Scale);
        Assert.Equal(new Vector2(originX, originY), layout.Layer.Origin);
    }

    [Fact]
    public void OnASurfaceTheFitGrew_TheLayerSitsAtItsSlackInsideThePresentedSurface()
    {
        // FixedHeight on a 2:1 window spans 360 world units across the 320-pixel canvas, so the
        // surface is 360 wide and the canvas sits 20 pixels into it.
        ScreenLayout layout = FrameLayout.Layout(
            (320, 180),
            View(Canvas, Canvas, ViewportFit.FixedHeight),
            1440,
            720);

        Assert.Equal((360, 180), layout.Surface);
        Assert.Equal(new Vector2(20f, 0f), layout.OnSurface.Origin);
        Assert.Equal(1f, layout.OnSurface.Scale);
        Assert.Equal(4f, layout.Present.Scale);
        Assert.Equal(new Vector2(80f, 0f), layout.Layer.Origin);
        Assert.Equal(Vector2.Zero, layout.Layer.ToCanvas(new Vector2(80f, 0f)));
    }

    private static FrameView View(Vector2 canvas, Vector2? cameraSize = null, ViewportFit fit = ViewportFit.Letterbox)
    {
        return new FrameView
        {
            Canvas = canvas,
            Sampling = TextureSampling.Point,
            Camera = new CameraView(Vector2.Zero, Vector2.Zero, cameraSize ?? canvas, fit),
        };
    }
}

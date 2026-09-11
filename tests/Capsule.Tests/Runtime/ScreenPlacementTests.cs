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
    public void TheCanvas_IsTheDeclaredRenderResolutionWhenThereIsOne()
    {
        Assert.Equal((320, 180), EngineOptions.CanvasOf((320, 180), 1280, 720));
    }

    [Fact]
    public void TheCanvas_IsTheConfiguredWindowWhenNoResolutionIsDeclared()
    {
        Assert.Equal((1024, 768), EngineOptions.CanvasOf(null, 1024, 768));
    }

    [Fact]
    public void WithNoRenderSurface_TheCanvasTakesItsOwnCentredFitOfTheWindow()
    {
        // Twice the canvas on X and three times on Y: the binding axis is X, and the bars are on Y.
        ScreenPlacement placement = FrameRenderer.WindowPlacement(Canvas, 640, 540);

        Assert.Equal(2f, placement.Scale);
        Assert.Equal(new Vector2(0f, 90f), placement.Origin);
        Assert.Equal(Vector2.Zero, placement.ToCanvas(new Vector2(0f, 90f)));
        Assert.Equal(new Vector2(160f, 90f), placement.ToCanvas(new Vector2(320f, 270f)));
    }

    [Fact]
    public void AWindowWithNoArea_PlacesNothing()
    {
        Assert.Equal(0f, FrameRenderer.WindowPlacement(Canvas, 0, 540).Scale);
        Assert.Equal(0f, FrameRenderer.WindowPlacement(Vector2.Zero, 640, 540).Scale);
    }

    [Fact]
    public void UnderPointSampling_TheRenderSurfaceTakesAWholeScale()
    {
        // 700 by 400 holds 320 by 180 twice over with a remainder the bars absorb.
        ScreenPlacement placement = FrameRenderer.TargetPlacement(TextureSampling.Point, 320, 180, 700, 400);

        Assert.Equal(2f, placement.Scale);
        Assert.Equal(new Vector2(30f, 20f), placement.Origin);
    }

    [Fact]
    public void UnderLinearSampling_TheRenderSurfaceFillsTheWindowItFits()
    {
        ScreenPlacement placement = FrameRenderer.TargetPlacement(TextureSampling.Linear, 320, 180, 700, 400);

        Assert.Equal(700f / 320f, placement.Scale);
        Assert.Equal(0f, placement.Origin.X);
    }

    [Fact]
    public void OnASurfaceNoLargerThanTheCanvas_TheLayerSitsAtItsCorner()
    {
        Assert.Equal(Vector2.Zero, FrameRenderer.ScreenSlack(320, 180, Canvas));
    }

    [Fact]
    public void OnASurfaceTheCameraGrew_TheLayerStaysTheCanvasCentredInIt()
    {
        // Expand and FixedHeight draw a wider surface to reveal more world; the layer is the canvas,
        // centred on whole pixels so it lands on the grid the world already used.
        Assert.Equal(new Vector2(40f, 0f), FrameRenderer.ScreenSlack(400, 180, Canvas));
        Assert.Equal(new Vector2(0f, 10f), FrameRenderer.ScreenSlack(320, 201, Canvas));
    }

    [Fact]
    public void ASampledMouse_ReadsACanvasPositionThroughTheWholeTwoStepPlacement()
    {
        // The layer sits at the slack inside the surface, and the surface is presented at its own
        // placement: a window pixel unwinds both at once.
        Vector2 slack = FrameRenderer.ScreenSlack(400, 180, Canvas);
        ScreenPlacement presented = FrameRenderer.TargetPlacement(TextureSampling.Point, 400, 180, 800, 360);
        ScreenPlacement layer = new(presented.Origin + (slack * presented.Scale), presented.Scale);

        Assert.Equal(2f, layer.Scale);
        Assert.Equal(new Vector2(80f, 0f), layer.Origin);
        Assert.Equal(Vector2.Zero, layer.ToCanvas(new Vector2(80f, 0f)));
        Assert.Equal(new Vector2(320f, 180f), layer.ToCanvas(new Vector2(720f, 360f)));
    }

    [Fact]
    public void AMouseOffTheLayer_ReadsOffTheCanvas()
    {
        ScreenPlacement placement = FrameRenderer.WindowPlacement(Canvas, 640, 540);

        // In the bar above the layer, and past its right edge.
        Assert.Equal(new Vector2(0f, -45f), placement.ToCanvas(Vector2.Zero));
        Assert.Equal(320.5f, placement.ToCanvas(new Vector2(641f, 90f)).X);
    }

    [Fact]
    public void BeforeAFrameHasDrawn_WindowPixelsAreCanvasPixels()
    {
        Assert.Equal(new Vector2(7f, 9f), ScreenPlacement.Identity.ToCanvas(new Vector2(7f, 9f)));
    }
}

using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// The camera converts between canvas pixels and the world through the host's own frame layout. A
// canvas point is followed down the route a sampled pointer takes in reverse: into the back buffer at
// the screen layer's placement, back through the presented surface, then off the world's fit.
public sealed class CameraCanvasTests
{
    private static readonly Vector2 Center = new(100f, 50f);
    private static readonly Vector2 Size = new(32f, 18f);
    private const int OutputWidth = 1000;
    private const int OutputHeight = 500;

    // A resolution of zero declares none. The output is wider than every canvas, so Expand and
    // FixedHeight grow the span and centre the canvas in a wider surface.
    [Theory]
    [InlineData(ViewportFit.Letterbox, 0, 0, 640f, 360f)]
    [InlineData(ViewportFit.Expand, 0, 0, 640f, 360f)]
    [InlineData(ViewportFit.FixedHeight, 0, 0, 640f, 360f)]
    [InlineData(ViewportFit.Letterbox, 320, 180, 320f, 180f)]
    [InlineData(ViewportFit.Expand, 320, 180, 320f, 180f)]
    [InlineData(ViewportFit.FixedHeight, 320, 180, 320f, 180f)]
    [InlineData(ViewportFit.Letterbox, 320, 180, 1280f, 720f)]
    [InlineData(ViewportFit.Expand, 320, 180, 1280f, 720f)]
    [InlineData(ViewportFit.FixedHeight, 320, 180, 1280f, 720f)]
    public void ACanvasPoint_MapsToTheWorldPointTheHostDrawsUnderIt(
        ViewportFit fit,
        int resolutionWidth,
        int resolutionHeight,
        float canvasWidth,
        float canvasHeight)
    {
        (int Width, int Height)? resolution = resolutionWidth > 0 ? (resolutionWidth, resolutionHeight) : null;
        Vector2 canvas = new(canvasWidth, canvasHeight);
        Camera camera = Settle(fit, resolution, canvas, scrollOrigin: Vector2.Zero);

        CameraView view = camera.ToView();
        ScreenLayout layout = FrameLayout.Layout(resolution, view, canvas, TextureSampling.Point, OutputWidth, OutputHeight);
        Rect drawn = view.Place(1f, layout.Span);

        Assert.Equal(drawn, camera.VisibleRegion);

        foreach (Vector2 point in new[] { Vector2.Zero, canvas / 2f, canvas, new Vector2(canvas.X * 0.25f, canvas.Y * 0.8f) })
        {
            Vector2 backBuffer = layout.Layer.Origin + (point * layout.Layer.Scale);
            Vector2 surface = resolution is null ? backBuffer : (backBuffer - layout.Present.Origin) / layout.Present.Scale;
            Vector2 world = new Vector2(drawn.Left, drawn.Top) + ((surface - new Vector2(layout.World.X, layout.World.Y)) / layout.World.Scale);

            AssertNear(world, camera.CanvasToWorld(point));
            AssertNear(point, camera.WorldToCanvas(camera.CanvasToWorld(point)));
        }
    }

    // The host draws a layer at factor f as if the camera's corner sat at ScrollLayout.Corner, and an
    // intent keeps its offset from that corner. The point on the layer is the one drawn over the
    // world point the plain overload names. A scroll origin moved after the settle takes effect at the
    // next one, as the host's frame does.
    [Fact]
    public void AScrollFactor_LandsOnThePointOfThatLayerDrawnUnderTheCanvasPoint()
    {
        Vector2 origin = new(40f, 20f);
        Camera camera = Settle(ViewportFit.Expand, (320, 180), new Vector2(320f, 180f), origin);
        camera.ScrollOrigin = new Vector2(-500f, 900f);
        Vector2 corner = new(camera.VisibleRegion.Left, camera.VisibleRegion.Top);
        Vector2 point = new(200f, 30f);

        foreach (Vector2 factor in new[] { Vector2.Zero, new Vector2(0.5f, 0.25f), Vector2.One, new Vector2(2f, -1f) })
        {
            Vector2 onLayer = camera.CanvasToWorld(point, factor);
            Vector2 drawnAt = corner + (onLayer - ScrollLayout.Corner(corner, origin, factor));

            AssertNear(camera.CanvasToWorld(point), drawnAt);
            AssertNear(point, camera.WorldToCanvas(onLayer, factor));
        }
    }

    private static Camera Settle(ViewportFit fit, (int Width, int Height)? resolution, Vector2 canvas, Vector2 scrollOrigin)
    {
        SceneFixtures.HookScene scene = new(start: opened =>
        {
            SceneFixtures.Open(opened, Center, Size);
            opened.Camera.Fit = fit;
            opened.Camera.ScrollOrigin = scrollOrigin;
        });

        // Left undisposed: stopping the scene releases the camera and the framing it settled.
        SceneSimulation simulation = new(
            scene,
            run: new Run { Canvas = canvas, Sampling = TextureSampling.Point, RenderResolution = resolution });
        simulation.Step(SceneFixtures.Step(output: new Vector2(OutputWidth, OutputHeight)));

        return scene.Camera;
    }

    private static void AssertNear(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 0.001f);
        Assert.Equal(expected.Y, actual.Y, 0.001f);
    }
}

using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Runtime;

// A 320 by 180 canvas over a 160 by 90 camera: two surface pixels per world unit on both axes.
public sealed class ViewportFitTests
{
    private const float PixelsPerUnit = 2f;
    private static readonly Vector2 Canvas = new(320f, 180f);
    private static readonly Vector2 Size = new(160f, 90f);

    // The grown axis is quantised down to a whole number of surface pixels, so the placed span
    // never exceeds the one the fit resolved, and the world sits on the surface at exactly the
    // declared scale — bars around it where the surface is the canvas the span fell short of —
    // whatever shape the window is: 8:7, 16:9, 16:10, a dragged 1300 by 1000, a window smaller
    // than the surface, and an ultrawide.
    [Theory]
    [InlineData(ViewportFit.Expand, 1280, 1120, 320, 280, 0, 0, 320, 280)]
    [InlineData(ViewportFit.Expand, 1280, 720, 320, 180, 0, 0, 320, 180)]
    [InlineData(ViewportFit.Expand, 1280, 800, 320, 200, 0, 0, 320, 200)]
    [InlineData(ViewportFit.Expand, 1300, 1000, 320, 246, 0, 0, 320, 246)]
    [InlineData(ViewportFit.Expand, 300, 170, 320, 180, 0, 0, 320, 180)]
    [InlineData(ViewportFit.Expand, 2560, 1080, 426, 180, 0, 0, 426, 180)]
    [InlineData(ViewportFit.FixedHeight, 1280, 1120, 320, 180, 57, 0, 205, 180)]
    [InlineData(ViewportFit.FixedHeight, 1280, 720, 320, 180, 0, 0, 320, 180)]
    [InlineData(ViewportFit.FixedHeight, 1280, 800, 320, 180, 16, 0, 288, 180)]
    [InlineData(ViewportFit.FixedHeight, 1300, 1000, 320, 180, 43, 0, 234, 180)]
    [InlineData(ViewportFit.FixedHeight, 300, 170, 320, 180, 1, 0, 317, 180)]
    [InlineData(ViewportFit.FixedHeight, 2560, 1080, 426, 180, 0, 0, 426, 180)]
    public void AGrowingFit_SpansWholeSurfacePixelsAtExactlyTheDeclaredScale(
        ViewportFit fit,
        int windowWidth,
        int windowHeight,
        int surfaceWidth,
        int surfaceHeight,
        int worldX,
        int worldY,
        int worldWidth,
        int worldHeight)
    {
        ScreenLayout layout = Layout((320, 180), View(fit), windowWidth, windowHeight);

        Assert.Equal((surfaceWidth, surfaceHeight), layout.Surface);
        Assert.Equal(new Letterbox(worldX, worldY, worldWidth, worldHeight, PixelsPerUnit), layout.World);
        Assert.Equal(worldWidth, layout.Span.X * PixelsPerUnit);
        Assert.Equal(worldHeight, layout.Span.Y * PixelsPerUnit);
        // Never past what the fit resolved, taken exactly: the float ResolveSpan itself can spell
        // an aspect like 365:180 a hair short of the integer count.
        (double exactX, double exactY) = ExactSpan(View(fit).Camera, windowWidth, windowHeight);
        Assert.True(layout.Span.X <= exactX && layout.Span.Y <= exactY);

        // The camera is placed on that same span, so the drawn rect and the surface agree.
        Rect world = View(fit).Camera.Place(1f, layout.Span);
        Assert.Equal(layout.Span.X, world.Right - world.Left);
        Assert.Equal(layout.Span.Y, world.Bottom - world.Top);
    }

    // A camera wider than its canvas — 200 by 90 on 320 by 180. FixedHeight binds the height, so
    // its scale is the canvas height over the camera's, 2, and its span is the one ResolveSpan
    // gives the window; Expand and Letterbox bind on the axis the canvas holds tighter, 1.6.
    [Theory]
    [InlineData(ViewportFit.FixedHeight, 1280, 720, 320, 180, 0, 0, 320, 180, 2f, 160f, 90f)]
    [InlineData(ViewportFit.FixedHeight, 1600, 400, 720, 180, 0, 0, 720, 180, 2f, 360f, 90f)]
    [InlineData(ViewportFit.Expand, 1280, 720, 320, 180, 0, 0, 320, 180, 1.6f, 200f, 112.5f)]
    [InlineData(ViewportFit.Expand, 1600, 400, 576, 180, 0, 18, 576, 144, 1.6f, 360f, 90f)]
    public void ACameraOfAnotherAspect_BindsOnTheAxisItsFitNames(
        ViewportFit fit,
        int windowWidth,
        int windowHeight,
        int surfaceWidth,
        int surfaceHeight,
        int worldX,
        int worldY,
        int worldWidth,
        int worldHeight,
        float scale,
        float spanX,
        float spanY)
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, new Vector2(200f, 90f), fit);
        FrameView view = new() { Canvas = Canvas, Sampling = TextureSampling.Point, Camera = camera };

        ScreenLayout layout = Layout((320, 180), view, windowWidth, windowHeight);

        Assert.Equal((surfaceWidth, surfaceHeight), layout.Surface);
        Assert.Equal(new Letterbox(worldX, worldY, worldWidth, worldHeight, scale), layout.World);
        Assert.Equal(new Vector2(spanX, spanY), layout.Span);
        Assert.Equal(worldWidth, layout.Span.X * scale);
        Assert.Equal(worldHeight, layout.Span.Y * scale);

        if (fit == ViewportFit.FixedHeight && windowWidth == 1280)
        {
            Assert.Equal(camera.ResolveSpan(new Vector2(1280f, 720f)), layout.Span);
        }
    }

    [Fact]
    public void LetterboxOnACameraOfAnotherAspect_BarsTheCanvasAtTheTighterAxis()
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, new Vector2(200f, 90f), ViewportFit.Letterbox);
        FrameView view = new() { Canvas = Canvas, Sampling = TextureSampling.Point, Camera = camera };

        ScreenLayout layout = Layout((320, 180), view, 1280, 720);

        Assert.Equal((320, 180), layout.Surface);
        Assert.Equal(new Vector2(200f, 90f), layout.Span);
        Assert.Equal(new Letterbox(0, 18, 320, 144, 1.6f), layout.World);
    }

    // At the widest output the cull region covers, 4:1, a surface rounded up would place a sliver
    // of world past the region: 400 units at 320/300 pixels a unit is 426.67 pixels, and 427 would
    // show 400.3125. Rounded down, the placed view stays inside what was culled against, and an
    // entity at its visible edge is drawn.
    [Fact]
    public void ThePlacedSpan_NeverReachesPastTheSweptBoundsAndTheEdgeEntityIsDrawn()
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, new Vector2(300f, 100f), ViewportFit.Expand);
        FrameView view = new() { Canvas = Canvas, Sampling = TextureSampling.Point, Camera = camera };

        ScreenLayout layout = Layout((320, 180), view, 1600, 400);
        Rect placed = camera.Place(1f, layout.Span);
        Rect swept = camera.SweptBounds;

        Assert.Equal((426, 180), layout.Surface);
        Assert.Equal(399.375f, layout.Span.X, 3);
        Assert.Equal(100f, layout.Span.Y);
        Assert.Equal(new Letterbox(0, 36, 426, 107, 320f / 300f), layout.World);
        Assert.Equal(400f, camera.ResolveSpan(new Vector2(1600f, 400f)).X);
        Assert.True(placed.Left >= swept.Left && placed.Right <= swept.Right);
        Assert.True(placed.Top >= swept.Top && placed.Bottom <= swept.Bottom);

        Scene scene = new();
        scene.Camera.ViewportSize = new Vector2(300f, 100f);
        scene.Camera.Fit = ViewportFit.Expand;
        Entity edge = new SceneFixtures.Drifter(new Vector2(placed.Right - 0.5f, 0f));
        edge.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8)));
        scene.Add(edge);
        using SimulationHost host = new(scene);

        Assert.Equal(1, host.Simulation.View.Sprites.Length);
    }

    [Theory]
    [InlineData(1280, 1120)]
    [InlineData(1280, 720)]
    [InlineData(1280, 800)]
    [InlineData(1300, 1000)]
    [InlineData(300, 170)]
    [InlineData(2560, 1080)]
    public void Letterbox_KeepsTheDeclaredSpanOnTheCanvasWithBars(int windowWidth, int windowHeight)
    {
        ScreenLayout layout = Layout((320, 180), View(ViewportFit.Letterbox), windowWidth, windowHeight);

        Assert.Equal((320, 180), layout.Surface);
        Assert.Equal(Size, layout.Span);
        Assert.Equal(Letterbox.Fit(Size.X, Size.Y, 320, 180), layout.World);
        Assert.Equal(PixelsPerUnit, layout.World.Scale);
    }

    // On a surface 181 pixels tall the camera's half-span is 45.25 units — a quarter pixel — so a
    // followed sprite advancing 0.3 units a step would flip between two pixels if it and the
    // camera were rounded apart. Snapped from the camera's corner it holds its pixel, a static
    // sprite recedes by whole pixels only, and a sprite at another sub-pixel phase never jitters
    // against it.
    [Fact]
    public void SnappedFromTheCamerasCorner_AFollowedSpriteHoldsItsPixelAndNothingJitters()
    {
        Vector2 half = new(80f, 45.25f);
        Vector2 statik = new(10f, 10f);
        float previousFollowed = float.NaN;
        float previousStatic = float.NaN;
        float previousPhase = float.NaN;

        for (int step = 0; step <= 40; step++)
        {
            Vector2 followed = new(0.3f * step, 0.3f * step);
            Vector2 phased = new(0.35f + (0.5f * step), 0.35f + (0.5f * step));
            Vector2 topLeft = followed - half;

            float followedPixel = PixelY(topLeft, followed);
            float staticPixel = PixelY(topLeft, statik);
            float phasePixel = PixelY(topLeft, phased) - staticPixel;

            Assert.Equal(MathF.Round(followedPixel), followedPixel);
            Assert.Equal(MathF.Round(staticPixel), staticPixel);
            Assert.Equal(MathF.Round(phasePixel), phasePixel);

            if (step > 0)
            {
                Assert.Equal(previousFollowed, followedPixel);
                Assert.True(staticPixel <= previousStatic && previousStatic - staticPixel <= 1f);
                Assert.True(phasePixel >= previousPhase && phasePixel - previousPhase <= 2f);
            }

            previousFollowed = followedPixel;
            previousStatic = staticPixel;
            previousPhase = phasePixel;
        }
    }

    // The grown axis's pixel count is the binding canvas extent times the output's ratio, taken in
    // integers, so neither float's shortfall nor its excess reaches it: 180 by 365 over 180 is
    // exactly 365 pixels where float spells 364.99997, and 180 by 930 over 1027 is 162.999, which
    // is 162 — the span 54 units, under the 54.333 the fit resolved — where a tolerance would have
    // rounded it up past the resolved span. The span is that count over the scale, exactly.
    [Theory]
    [InlineData(ViewportFit.Expand, 365, 180, 365)]
    [InlineData(ViewportFit.FixedHeight, 365, 180, 365)]
    [InlineData(ViewportFit.Expand, 1957, 1027, 342)]
    [InlineData(ViewportFit.FixedHeight, 930, 1027, 162)]
    public void TheGrownAxis_CountsWholePixelsInIntegersNeitherShortNorPastTheResolvedSpan(
        ViewportFit fit,
        int windowWidth,
        int windowHeight,
        int grownPixels)
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, new Vector2(100f, 60f), fit);
        FrameView view = new() { Canvas = Canvas, Sampling = TextureSampling.Point, Camera = camera };
        Vector2 window = new(windowWidth, windowHeight);

        Assert.Equal((grownPixels, null), FrameLayout.GrownPixels(camera, (320, 180), camera.ResolveSpan(window), 3f, windowWidth, windowHeight));

        ScreenLayout layout = Layout((320, 180), view, windowWidth, windowHeight);
        int surfaceWidth = Math.Max(320, grownPixels);

        Assert.Equal((surfaceWidth, 180), layout.Surface);
        Assert.Equal(new Letterbox((surfaceWidth - grownPixels) / 2, 0, grownPixels, 180, 3f), layout.World);
        Assert.Equal(grownPixels / 3f, layout.Span.X);
        Assert.Equal(60f, layout.Span.Y);
        Assert.True(layout.Span.X <= ExactSpan(camera, windowWidth, windowHeight).X);
    }

    // Where the canvas holds the grown axis tighter than the other, the camera's size does not
    // cancel and the count is the true fraction in double: 4 × 320 × 5746 over 9 × 1593 is
    // 512.99997, which float's product rounds up to 513 — one pixel past the resolved span — and
    // double floors to 512; 4 × 320 × 4500 over 9 × 1280 is exactly 500 and loses nothing; and
    // 30.6 × 320 × 7722 over 57.2 × 3264 is 404.99999965, a third of a millionth under, which a
    // relative billionth would have lifted to 405 and four ulps leave at 404.
    [Theory]
    [InlineData(9f, 4f, 5746, 1593, 512)]
    [InlineData(9f, 4f, 4500, 1280, 500)]
    [InlineData(57.2f, 30.6f, 7722, 3264, 404)]
    public void ANonCancellingGrownAxis_FloorsTheTrueFractionNotTheFloatProduct(
        float sizeX,
        float sizeY,
        int windowWidth,
        int windowHeight,
        int grownPixels)
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, new Vector2(sizeX, sizeY), ViewportFit.Expand);
        FrameView view = new() { Canvas = Canvas, Sampling = TextureSampling.Point, Camera = camera };
        Vector2 window = new(windowWidth, windowHeight);
        float scale = 320f / sizeX;

        Assert.Equal(scale, FrameLayout.PixelsPerUnit((320, 180), camera));
        Assert.Equal((grownPixels, null), FrameLayout.GrownPixels(camera, (320, 180), camera.ResolveSpan(window), scale, windowWidth, windowHeight));

        ScreenLayout layout = Layout((320, 180), view, windowWidth, windowHeight);

        Assert.Equal(grownPixels, layout.Surface.Width);
        Assert.Equal(grownPixels, layout.World.Width);
        Assert.Equal(scale, layout.World.Scale);
        Assert.Equal(grownPixels / scale, layout.Span.X);
        Assert.True(layout.Span.X <= ExactSpan(camera, windowWidth, windowHeight).X);
    }

    // ResolveSpan's rule in double precision: the span the fit truly resolved, which float's own
    // ResolveSpan can spell a hair either side of.
    private static (double X, double Y) ExactSpan(in CameraView camera, int windowWidth, int windowHeight)
    {
        double aspect = (double)windowWidth / windowHeight;
        double sizeX = camera.Size.X;
        double sizeY = camera.Size.Y;

        return camera.Fit switch
        {
            ViewportFit.Letterbox => (sizeX, sizeY),
            ViewportFit.FixedHeight => (sizeY * aspect, sizeY),
            _ => aspect > sizeX / sizeY ? (sizeY * aspect, sizeY) : (sizeX, sizeX / aspect),
        };
    }

    // A canvas the clamp keeps far wider than the span the fit resolved: the span is quantised,
    // not the surface, so the 100 by 100 view is placed centred in the 500 by 100 surface with bars
    // beside it, still inside the 400 by 400 the camera culled against, and the entities at its
    // edges are drawn.
    [Fact]
    public void ASurfaceTheCanvasClampEnlarged_BarsTheQuantisedSpanRatherThanStretchingIt()
    {
        CameraView camera = new(Vector2.Zero, Vector2.Zero, new Vector2(100f, 100f), ViewportFit.Expand);
        FrameView view = new() { Canvas = new Vector2(500f, 100f), Sampling = TextureSampling.Point, Camera = camera };

        ScreenLayout layout = Layout((500, 100), view, 500, 500);
        Rect placed = camera.Place(1f, layout.Span);
        Rect swept = camera.SweptBounds;

        Assert.Equal((500, 100), layout.Surface);
        Assert.Equal(new Vector2(100f, 100f), layout.Span);
        Assert.Equal(new Letterbox(200, 0, 100, 100, 1f), layout.World);
        Assert.Equal(new Rect(-50f, -50f, 50f, 50f), placed);
        Assert.Equal(new Rect(-200f, -200f, 200f, 200f), swept);

        Scene scene = new();
        scene.Camera.ViewportSize = new Vector2(100f, 100f);
        scene.Camera.Fit = ViewportFit.Expand;
        foreach (float x in new[] { -49f, 49f })
        {
            Entity edge = new SceneFixtures.Drifter(new Vector2(x, 0f));
            edge.Add(new SpriteRenderer(SceneFixtures.Frame(2, 2)));
            scene.Add(edge);
        }

        using SimulationHost host = new(scene);

        Assert.Equal(2, host.Simulation.View.Sprites.Length);
    }

    // With the camera still, a sprite advancing half a unit a step — one pixel — lands one pixel
    // further each step, never two and never none.
    [Fact]
    public void SnappedFromAStillCamera_ASpriteAdvancingOnePixelAStepLandsOnePixelFurther()
    {
        Vector2 topLeft = new(-80.25f, -45f);
        float previous = PixelX(topLeft, Vector2.Zero);

        for (int step = 1; step <= 20; step++)
        {
            float pixel = PixelX(topLeft, new Vector2(0.5f * step, 0f));

            Assert.Equal(previous + 1f, pixel);
            previous = pixel;
        }
    }

    // The sprite's surface pixel from the camera's corner, along one axis.
    private static float PixelX(Vector2 topLeft, Vector2 position) => Surface(topLeft, position).X;

    private static float PixelY(Vector2 topLeft, Vector2 position) => Surface(topLeft, position).Y;

    private static Vector2 Surface(Vector2 topLeft, Vector2 position) =>
        PixelGrid.SnapOffset(topLeft, position, PixelsPerUnit) * PixelsPerUnit;

    private static FrameView View(ViewportFit fit) => new()
    {
        Canvas = Canvas,
        Sampling = TextureSampling.Point,
        Camera = new CameraView(Vector2.Zero, Vector2.Zero, Size, fit),
    };

    private static ScreenLayout Layout((int Width, int Height)? resolution, FrameView view, int outputWidth, int outputHeight) =>
        FrameLayout.Layout(resolution, view.Camera, view.Canvas, view.Sampling, outputWidth, outputHeight);
}

using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

// How a frame drawn between two fixed steps places a sprite: on a pixel-perfect frame, snapped at both ends of
// the step and carried between them in whole pixels, and otherwise interpolated as it stands. Each frame here is
// resolved from its camera as the renderer resolves it, and each sprite placed as the vertex stage places it.
public sealed class PixelPerfectMotionTests
{
    private static readonly (int Width, int Height) Surface = (320, 180);

    private static readonly Vector2 Span = new(320f, 180f);

    // A camera whose corner moves 0.4 to 0.6 of a pixel across the step, rounding to a different pixel at each end.
    private static readonly CameraView Panning = new(new Vector2(160.4f, 90f), new Vector2(160.6f, 90f), Span);

    private static IEnumerable<float> Alphas() => Enumerable.Range(0, 241).Select(static step => step / 240f);

    // Two sprites a fraction of a pixel apart land on the same pixels at both ends of the step. They are one
    // picture, and change pixel on the same frame at every fraction of the step between.
    [Fact]
    public void TwoSpritesWithTheSameStepEndPixels_ChangePixelOnTheSameFrame()
    {
        CameraView camera = new(new Vector2(160.3f, 90f), new Vector2(163.9f, 90f), Span);
        (Vector2 Previous, Vector2 Current) early = (new Vector2(40.2f, 20f), new Vector2(51.1f, 20f));
        (Vector2 Previous, Vector2 Current) late = (new Vector2(40.4f, 20f), new Vector2(51.4f, 20f));

        Assert.Equal(Drawn(camera, 0f, early), Drawn(camera, 0f, late));
        Assert.Equal(Drawn(camera, 1f, early), Drawn(camera, 1f, late));
        Assert.All(Alphas(), alpha => Assert.Equal(Drawn(camera, alpha, early), Drawn(camera, alpha, late)));
    }

    // A rider a different fraction of a pixel from the camera at each end of the step, on the same whole pixel of
    // the view at both, holds that pixel at every fraction between while the view crosses one.
    [Fact]
    public void ASpriteHoldingItsPixelOnTheViewAtBothEnds_HoldsItAtEveryAlpha()
    {
        (Vector2 Previous, Vector2 Current) rider = (new Vector2(5.45f, 40f), new Vector2(6.45f, 40f));

        Assert.Equal(new Vector2(5f, 40f), Drawn(Panning, 0f, rider));
        Assert.All(Alphas(), alpha => Assert.Equal(new Vector2(5f, 40f), Drawn(Panning, alpha, rider)));
    }

    // At the step's end the frame draws what a frame placed from that step alone draws: the corner snapped, and the
    // sprite snapped from its anchor. A held run and a capture draw there. The zooming camera changes scale across
    // the step, and the frame far out holds positions least finely.
    [Theory]
    [InlineData(160.3f, 163.9f, 1f, 41.3f, 52.75f)]
    [InlineData(160.3f, 158.15f, 1.25f, 41.3f, 30.6f)]
    [InlineData(9001.7f, 9004.35f, 1f, 9010.55f, 9012.2f)]
    public void AtAlphaOne_TheFrameDrawsWhatTheStepsEndAloneDraws(float from, float to, float zoom, float sprite, float moved)
    {
        CameraView camera = new(new Vector2(from, 90f), new Vector2(to, 90f), Span / zoom) { PreviousSize = Span };
        (WorldEnds ends, float scale) = Frame(camera, 1f, pixelPerfect: true);
        Rect world = camera.At(1f).Place(1f, Span / zoom);
        (Vector2 frame, Vector2 anchor) = PixelGrid.Corners(world.Position, scale);
        Vector2 position = new(moved, 30.3f);
        Vector2 pixels = PixelGrid.Pixels(anchor, position, scale);

        Assert.Equal(frame, ends.Corner);
        Assert.Equal(
            frame + new Vector2((float)(pixels.X / (double)scale), (float)(pixels.Y / (double)scale)),
            ends.Base.Place(new Vector2(sprite, 30.3f), position));
    }

    // A point held through the step under a still placement is counted once, and lands where its two ends carried
    // between would, out past the whole pixel counts a float holds exactly.
    [Fact]
    public void AStillPointUnderAStillPlacement_LandsWhereItsTwoEndsWould()
    {
        GridPlacement still = new(new Vector2(10.3f, 4.7f), new Vector2(10.3f, 4.7f), new Vector2(10f, 5f), Vector2.Zero, new Vector2(3f), 3f, 0.37f);
        for (int i = 0; i < 400; i++)
        {
            Vector2 point = new(i * 0.137f, i * -0.291f);
            Vector2 pixels = PixelGrid.Between(PixelGrid.Pixels(still.PreviousLayerCorner, point, 3f), PixelGrid.Pixels(still.LayerCorner, point, 3f), still.Alpha);

            Assert.Equal(still.FrameCorner + new Vector2((float)(pixels.X / 3.0), (float)(pixels.Y / 3.0)), still.Place(point, point));
        }

        // 16 777 221 pixels out, a count a float rounds to 16 777 220.
        GridPlacement origin = new(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, new Vector2(3f), 3f, 0.37f);
        Vector2 far = new(5592407f, 0f);
        Assert.Equal(new Vector2(5592406.5f, 0f), origin.Place(far, far));
    }

    // A frame that is not pixel-perfect places the camera and every sprite where each stands at the frame's
    // fraction of the step, unsnapped.
    [Fact]
    public void AFrameThatIsNotPixelPerfect_InterpolatesUnsnapped()
    {
        (Vector2 Previous, Vector2 Current) sprite = (new Vector2(40.2f, 20.7f), new Vector2(51.1f, 23.3f));

        Assert.All(Alphas(), alpha =>
        {
            (WorldEnds ends, float scale) = Frame(Panning, alpha, pixelPerfect: false);
            Vector2 corner = Panning.At(alpha).Place(1f, Span).Position;
            Vector2 at = StepInterpolation.Interpolate(sprite.Previous, sprite.Current, alpha);

            Assert.Equal(corner, ends.Corner);

            // The offset from the corner, added back to it, rounds as the vertex stage rounds it.
            Assert.Equal(corner + (at - corner), ends.Base.Place(sprite.Previous, sprite.Current));
        });
    }

    // The surface pixel a sprite at its two step ends is drawn on by a pixel-perfect frame at alpha.
    private static Vector2 Drawn(in CameraView camera, float alpha, (Vector2 Previous, Vector2 Current) sprite)
    {
        (WorldEnds ends, float scale) = Frame(camera, alpha, pixelPerfect: true);

        return (ends.Base.Place(sprite.Previous, sprite.Current) - ends.Corner) * scale;
    }

    // The ends a frame at alpha places its world from on the 320 by 180 surface, laid out as the renderer lays it out.
    private static (WorldEnds Ends, float Scale) Frame(in CameraView camera, float alpha, bool pixelPerfect)
    {
        FrameView view = new() { Camera = camera, PixelPerfect = pixelPerfect, Canvas = Span };
        CameraView at = camera.At(alpha);
        ScreenLayout layout = FrameLayout.Layout(Surface, at, view.Canvas, Surface.Width, Surface.Height, integerScaling: false);
        Rect world = at.Place(1f, layout.Span);

        return (WorldEnds.Resolve(view, alpha, Surface, Surface.Width, Surface.Height, factor: 1, in world, layout.Span, layout.World), layout.World.Scale);
    }
}

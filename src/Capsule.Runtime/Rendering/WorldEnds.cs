using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Runtime.Rendering;

// Where a frame places the world through a step. Corner is the world point at the viewport's corner, which the
// picture is placed from, and Base places the world's own layer. Each end's parallax moves a scrolled layer's
// corners from Base's, as ScrollLayout.Corner does. A frame that does not snap holds the frame at alpha as both
// ends.
internal readonly record struct WorldEnds(Vector2 Corner, GridPlacement Base, Vector2 PreviousParallax, Vector2 Parallax)
{
    // placement moved onto the layer at factor, its corners at each end of the step.
    internal GridPlacement Layer(in GridPlacement placement, Vector2 factor) => placement with
    {
        PreviousLayerCorner = ScrollLayout.Corner(Base.PreviousLayerCorner, PreviousParallax, factor),
        LayerCorner = ScrollLayout.Corner(Base.LayerCorner, Parallax, factor),
    };

    // The ends a frame at alpha places its world from. world, span and fit are the frame's own at alpha, laid out
    // for an output of outputWidth by outputHeight on resolution's surface, or on the window where it is null, and
    // enlarged by factor. A pixel-perfect frame snaps each end's corner at that end's scale, as PixelGrid.Corners
    // does, and draws the picture where a point the world holds still lands between them. Under a change of scale
    // across the step, a zoom, nothing holds still, and the picture is placed from the corner at alpha snapped alone.
    internal static WorldEnds Resolve(
        FrameView view,
        float alpha,
        (int Width, int Height)? resolution,
        int outputWidth,
        int outputHeight,
        int factor,
        in Rect world,
        Vector2 span,
        in Letterbox fit)
    {
        Vector2 corner = new(world.Left, world.Top);
        Vector2 anchor = corner;
        if (view.PixelPerfect)
        {
            (corner, anchor) = PixelGrid.Corners(corner, fit.Scale);
        }

        CameraView camera = view.Camera;
        Vector2 parallax = ScrollLayout.Parallax(corner, span, camera.ScrollCenter);
        GridPlacement still = new(anchor, anchor, corner, Vector2.Zero, new Vector2(fit.Scale), view.PixelPerfect ? fit.Scale : 0f, alpha);
        WorldEnds ends = new(corner, still, parallax, parallax);
        bool held = camera.PreviousCenter == camera.Center && camera.PreviousSize == camera.Size && camera.PreviousOffset == camera.Offset;

        // A held camera skips laying out its two ends, which are the frame's own.
        if (!view.PixelPerfect || held)
        {
            return ends;
        }

        (Vector2 previousCorner, Vector2 previousSpan, Letterbox previousFit) = End(camera, 0f, view, resolution, outputWidth, outputHeight, factor);
        (Vector2 currentCorner, Vector2 currentSpan, Letterbox currentFit) = End(camera, 1f, view, resolution, outputWidth, outputHeight, factor);
        if (!(previousFit.Scale > 0f) || !(currentFit.Scale > 0f) || previousFit.IsEmpty || currentFit.IsEmpty)
        {
            return ends;
        }

        (Vector2 previousFrame, Vector2 previousAnchor) = PixelGrid.Corners(previousCorner, previousFit.Scale);
        (Vector2 currentFrame, Vector2 currentAnchor) = PixelGrid.Corners(currentCorner, currentFit.Scale);
        Vector2 previousOrigin = new(previousFit.X, previousFit.Y);

        return new WorldEnds(
            corner,
            still with
            {
                PreviousLayerCorner = previousAnchor,
                LayerCorner = currentAnchor,
                FrameCorner = corner + ((previousOrigin - new Vector2(fit.X, fit.Y)) / fit.Scale),
                Shift = new Vector2(currentFit.X, currentFit.Y) - previousOrigin,
                Grids = new Vector2(previousFit.Scale, currentFit.Scale),
            },
            ScrollLayout.Parallax(previousFrame, previousSpan, camera.ScrollCenter),
            ScrollLayout.Parallax(currentFrame, currentSpan, camera.ScrollCenter));
    }

    // The world rect's corner, span and fit at one end of the step, as the frame lays them out at alpha.
    private static (Vector2 Corner, Vector2 Span, Letterbox Fit) End(
        in CameraView camera,
        float end,
        FrameView view,
        (int Width, int Height)? resolution,
        int outputWidth,
        int outputHeight,
        int factor)
    {
        CameraView at = camera.At(end);
        ScreenLayout layout = FrameLayout.Layout(resolution, at, view.Canvas, outputWidth, outputHeight, view.IntegerScaling);
        Rect world = at.Place(1f, layout.Span);
        Letterbox fit = resolution is null ? layout.World : FrameLayout.Enlarge(layout, factor).World;

        return (new Vector2(world.Left, world.Top), layout.Span, fit);
    }
}

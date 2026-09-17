using Capsule.Rendering;
using Vector2 = System.Numerics.Vector2;

namespace Capsule.Runtime.Rendering;

// The presentation geometry a frame is drawn on, as pure arithmetic over a view and a back buffer:
// no device, no state, nothing drawn. Drawing and pointer mapping resolve from the same geometry,
// or a sampled window position comes back as the wrong canvas pixel.
//
// The rule the whole block answers to: a grown axis is never rounded a pixel up, so the span the
// world is placed on never exceeds the one the fit resolved — which is what the camera culled
// against — and a surface left larger gets bars around it rather than showing world the camera
// never considered.
internal static class FrameLayout
{
    // The whole geometry for view on a back buffer of this extent, on both paths: the canvas
    // letterboxed straight into the back buffer, and the render surface presented into it where a
    // resolution is declared.
    internal static ScreenLayout Layout(
        (int Width, int Height)? renderResolution,
        FrameView view,
        int outputWidth,
        int outputHeight)
    {
        // The window is the output the fit answers to on both paths: a declared canvas is derived
        // from the resolved rect rather than being the thing that shapes it. The span travels beside
        // the rect because subtracting the rect's edges loses precision far from the origin, and the
        // scale it feeds is what quantises every sprite to the pixel grid.
        Vector2 span = view.Camera.ResolveSpan(new Vector2(outputWidth, outputHeight));

        if (renderResolution is not { } resolution)
        {
            ScreenPlacement windowed = WindowPlacement(view.Canvas, outputWidth, outputHeight);

            return new ScreenLayout(
                span,
                (outputWidth, outputHeight),
                Letterbox.Fit(span.X, span.Y, outputWidth, outputHeight),
                windowed,
                default,
                windowed,
                ScreenOnSurface: true);
        }

        (int Width, int Height) surface = SurfaceSize(resolution, view.Camera, span, outputWidth, outputHeight);
        float pixelsPerUnit = PixelsPerUnit(resolution, view.Camera);
        span = QuantisedSpan(view.Camera, resolution, span, pixelsPerUnit, surface, outputWidth, outputHeight);
        Letterbox world = WorldFit(view.Camera, span, pixelsPerUnit, surface);
        ScreenPlacement presented = TargetPlacement(view.Sampling, surface.Width, surface.Height, outputWidth, outputHeight);

        // A canvas declared apart from the resolution is not in the surface's pixels: drawn on the
        // surface it would be cropped or left unscaled, so it takes its own centred fit of the
        // window over the presented surface, as it does with no surface at all.
        if (view.Canvas != new Vector2(resolution.Width, resolution.Height))
        {
            ScreenPlacement windowed = WindowPlacement(view.Canvas, outputWidth, outputHeight);

            return new ScreenLayout(span, surface, world, default, presented, windowed, ScreenOnSurface: false);
        }

        Vector2 slack = ScreenSlack(surface.Width, surface.Height, view.Canvas);

        return new ScreenLayout(
            span,
            surface,
            world,
            new ScreenPlacement(slack, 1f),
            presented,
            presented.Scale > 0f
                ? new ScreenPlacement(presented.Origin + (slack * presented.Scale), presented.Scale)
                : default,
            ScreenOnSurface: true);
    }

    // The span the world is placed on. Under Letterbox the declared span, as ever. Under Expand or
    // FixedHeight the axis the fit grew is quantised down to whole surface pixels at the declared
    // scale, and to what the surface holds, while the binding axis stays the camera's own.
    internal static Vector2 QuantisedSpan(
        in CameraView camera,
        (int Width, int Height) canvas,
        Vector2 span,
        float pixelsPerUnit,
        (int Width, int Height) surface,
        int outputWidth,
        int outputHeight)
    {
        if (camera.Fit == ViewportFit.Letterbox || !(pixelsPerUnit > 0f))
        {
            return span;
        }

        (int? grownX, int? grownY) = GrownPixels(camera, canvas, span, pixelsPerUnit, outputWidth, outputHeight);

        return new Vector2(
            grownX is { } x ? Math.Min(x, surface.Width) / pixelsPerUnit : camera.Size.X,
            grownY is { } y ? Math.Min(y, surface.Height) / pixelsPerUnit : camera.Size.Y);
    }

    // The whole surface pixels the fit's grown axis covers, null on an axis it did not grow — the
    // one count the grown axis's surface extent and its quantised span are both taken from, so the
    // two cannot disagree. Where the scale is what the other axis implies, the camera's size
    // cancels and the count is the binding canvas extent times the output's ratio, taken exactly
    // in integers; where the canvas holds the grown axis tighter than the other, it is the floor
    // of the true fraction evaluated once in double from the source quantities, never of a float
    // product that may already have rounded up past it.
    internal static (int? X, int? Y) GrownPixels(
        in CameraView camera,
        (int Width, int Height) canvas,
        Vector2 span,
        float pixelsPerUnit,
        int outputWidth,
        int outputHeight)
    {
        if (camera.Fit == ViewportFit.Letterbox || outputWidth <= 0 || outputHeight <= 0)
        {
            return (null, null);
        }

        Vector2 size = camera.Size;
        bool heightBinds = camera.Fit == ViewportFit.FixedHeight || canvas.Height / size.Y <= canvas.Width / size.X;
        bool widthBinds = camera.Fit == ViewportFit.Expand && canvas.Width / size.X <= canvas.Height / size.Y;

        if (camera.Fit == ViewportFit.FixedHeight || span.X > size.X)
        {
            int count = heightBinds
                ? (int)((long)canvas.Height * outputWidth / outputHeight)
                : FloorExact((double)size.Y * canvas.Width * outputWidth / ((double)size.X * outputHeight));

            return (count, null);
        }

        if (span.Y > size.Y)
        {
            int count = widthBinds
                ? (int)((long)canvas.Width * outputHeight / outputWidth)
                : FloorExact((double)size.X * canvas.Height * outputHeight / ((double)size.Y * outputWidth));

            return (null, count);
        }

        return (null, null);
    }

    // The floor of a pixel count computed in double, guarded only against double's own rounding:
    // the quotient is four operations, each within half an ulp, so a count that is truly a whole
    // number can land at most a few ulps under it, and lifting the value by four ulps recovers
    // exactly that and nothing else — a relative or absolute epsilon, however small, is wider
    // than some genuinely fractional gap a window's integers and a camera's size can produce.
    private static int FloorExact(double pixels)
    {
        for (int ulp = 0; ulp < 4; ulp++)
        {
            pixels = Math.BitIncrement(pixels);
        }

        return (int)Math.Floor(pixels);
    }

    // Where the world lands on the render surface. Under Letterbox the declared span is fitted into
    // the surface at whatever scale it allows and the slack is bars, as ever. Under Expand or
    // FixedHeight the quantised span is a whole number of surface pixels at exactly the declared
    // pixels per unit, so it is placed at that stated scale — never one recomputed from a division
    // that can land an ulp off — centred, with bars where the surface is larger than it.
    internal static Letterbox WorldFit(in CameraView camera, Vector2 span, float pixelsPerUnit, (int Width, int Height) surface) =>
        camera.Fit == ViewportFit.Letterbox || !(pixelsPerUnit > 0f)
            ? Letterbox.Fit(span.X, span.Y, surface.Width, surface.Height)
            : Letterbox.FitAt(span.X, span.Y, surface.Width, surface.Height, pixelsPerUnit);

    // Half of what the surface has over the canvas, in whole surface pixels: under Expand or
    // FixedHeight the surface grows past the canvas to reveal more world, and the screen layer stays
    // the canvas, centred in it.
    internal static Vector2 ScreenSlack(int surfaceWidth, int surfaceHeight, Vector2 canvas) => new(
        MathF.Max(MathF.Floor((surfaceWidth - canvas.X) / 2f), 0f),
        MathF.Max(MathF.Floor((surfaceHeight - canvas.Y) / 2f), 0f));

    // Where the canvas lands straight in the back buffer, with no render surface between them: its
    // own centred fit, so the layer keeps its aspect and its place whatever the camera's fit did
    // with the world behind it. A scale of 0 is a canvas or a window with no area.
    internal static ScreenPlacement WindowPlacement(Vector2 canvas, int outputWidth, int outputHeight)
    {
        Letterbox fit = Letterbox.Fit(canvas.X, canvas.Y, outputWidth, outputHeight);

        return fit.IsEmpty ? default : new ScreenPlacement(new Vector2(fit.X, fit.Y), fit.Scale);
    }

    // Where the render surface's own top-left corner lands in the back buffer, on the fit its
    // sampling mode calls for. A scale of 0 is a surface or a back buffer with no area.
    internal static ScreenPlacement TargetPlacement(
        TextureSampling sampling,
        int targetWidth,
        int targetHeight,
        int containerWidth,
        int containerHeight)
    {
        Letterbox fit = PresentFit(sampling, targetWidth, targetHeight, containerWidth, containerHeight);
        if (fit.IsEmpty)
        {
            return default;
        }

        // The fit's own whole-pixel corner, not the exact centre: a bar of an odd number of pixels
        // centres on a half pixel, which under point sampling puts every texel boundary on a pixel
        // centre and leaves the fill rule to break a tie per row. Linear sampling answers to no
        // pixel grid, so the half pixel the rounded corner gives up is invisible there.
        //
        // The scale travels as one scalar rather than the fit's extents becoming a destination
        // rectangle, whose two extents would round independently and skew the blit.
        return new ScreenPlacement(new Vector2(fit.X, fit.Y), fit.Scale);
    }

    // The surface a declared canvas draws on for a resolved world rect. Pixels per world unit are
    // whatever the canvas gives the camera's declared span on the fit's binding axis, so the fit
    // changes how much world is on the surface and never how large a world unit is on it. Under
    // Letterbox the resolved rect is that declared span, so the surface is the canvas exactly. It
    // never shrinks below the canvas, and never exceeds the back buffer on an axis: past that the
    // present can only scale the extra pixels back down, so they buy nothing and cost the whole
    // surface every frame.
    internal static (int Width, int Height) SurfaceSize(
        (int Width, int Height) canvas,
        in CameraView camera,
        Vector2 resolvedSpan,
        int outputWidth,
        int outputHeight)
    {
        float pixelsPerUnit = PixelsPerUnit(canvas, camera);
        if (!(pixelsPerUnit > 0f) || !(resolvedSpan.X > 0f) || !(resolvedSpan.Y > 0f))
        {
            return canvas;
        }

        (int? grownX, int? grownY) = GrownPixels(camera, canvas, resolvedSpan, pixelsPerUnit, outputWidth, outputHeight);

        return (
            Extent(grownX ?? (int)MathF.Floor(resolvedSpan.X * pixelsPerUnit), canvas.Width, outputWidth),
            Extent(grownY ?? (int)MathF.Floor(resolvedSpan.Y * pixelsPerUnit), canvas.Height, outputHeight));
    }

    // Surface pixels per world unit: what the canvas gives the camera's declared span on the axis
    // its fit binds — the height under FixedHeight, whose height is exact; whichever axis the canvas
    // holds tighter otherwise, the scale Letterbox draws at and Expand keeps. Zero for a span with
    // no area.
    internal static float PixelsPerUnit((int Width, int Height) canvas, in CameraView camera)
    {
        Vector2 size = camera.Size;
        if (!(size.X > 0f) || !(size.Y > 0f))
        {
            return 0f;
        }

        return camera.Fit == ViewportFit.FixedHeight
            ? canvas.Height / size.Y
            : MathF.Min(canvas.Width / size.X, canvas.Height / size.Y);
    }

    // Which fit the render surface takes into the back buffer. Point sampling owes its source
    // pixels a square block each, so it takes the whole scale and lets the bars absorb the
    // remainder; linear sampling answers to no pixel grid and fills the window.
    internal static Letterbox PresentFit(
        TextureSampling sampling,
        int targetWidth,
        int targetHeight,
        int containerWidth,
        int containerHeight) =>
        sampling == TextureSampling.Point
            ? Letterbox.FitPixels(targetWidth, targetHeight, containerWidth, containerHeight)
            : Letterbox.Fit(targetWidth, targetHeight, containerWidth, containerHeight);

    // GrownPixels' count on an axis the fit grew, so the surface is exactly the quantised span
    // there, never a pixel more that would show as a one-pixel bar; the clamp's floor absorbs a
    // binding axis whose float product lands under the canvas it equals.
    private static int Extent(int pixels, int canvas, int output) =>
        Math.Clamp(pixels, canvas, Math.Max(canvas, output));
}

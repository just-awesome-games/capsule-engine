using System.Numerics;

namespace Capsule.Rendering;

// The presentation geometry a frame is drawn on, as arithmetic over a camera, a canvas and a back
// buffer. No device, no state, nothing drawn. Drawing, pointer mapping and the camera's canvas to world
// conversion resolve from the same geometry, or a point maps to the wrong pixel.
//
// The rule every method here follows: a grown axis is never rounded a pixel up, so the span the
// world is placed on stays within the span the fit resolved and the camera culled against. A larger
// surface gets bars around it instead of showing world the camera never considered.
internal static class FrameLayout
{
    // The geometry for a camera and canvas on a back buffer of this extent. Two paths: the canvas
    // letterboxed straight into the back buffer, and the render surface presented into it where a
    // resolution is declared.
    internal static ScreenLayout Layout(
        (int Width, int Height)? renderResolution,
        in CameraView camera,
        Vector2 canvas,
        int outputWidth,
        int outputHeight)
    {
        // The fit answers to the window on both paths, and a declared canvas is derived from the
        // resolved rect. The span travels beside the rect because subtracting the rect's edges loses
        // precision far from the origin, and its scale quantises every sprite to the pixel grid.
        Vector2 span = camera.ResolveSpan(new Vector2(outputWidth, outputHeight));

        if (renderResolution is not { } resolution)
        {
            ScreenPlacement windowed = WindowPlacement(canvas, outputWidth, outputHeight);

            return new ScreenLayout(
                span,
                (outputWidth, outputHeight),
                Letterbox.Fit(span.X, span.Y, outputWidth, outputHeight),
                windowed,
                default,
                windowed,
                ScreenOnSurface: true);
        }

        (int Width, int Height) surface = SurfaceSize(resolution, camera, span, outputWidth, outputHeight);
        float pixelsPerUnit = PixelsPerUnit(resolution, camera);
        span = QuantisedSpan(camera, resolution, span, pixelsPerUnit, surface, outputWidth, outputHeight);
        Letterbox world = WorldFit(camera, span, pixelsPerUnit, surface);
        ScreenPlacement presented = TargetPlacement(surface.Width, surface.Height, outputWidth, outputHeight);

        // A canvas declared apart from the resolution is not in the surface's pixels. Drawn on the
        // surface it would be cropped or left unscaled, so it takes its own centred fit of the
        // window over the presented surface.
        if (canvas != new Vector2(resolution.Width, resolution.Height))
        {
            ScreenPlacement windowed = WindowPlacement(canvas, outputWidth, outputHeight);

            return new ScreenLayout(span, surface, world, default, presented, windowed, ScreenOnSurface: false);
        }

        Vector2 slack = ScreenSlack(surface.Width, surface.Height, canvas);

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

    // The span the world is placed on. Under Letterbox it is the declared span. Under Expand or
    // FixedHeight the grown axis is quantised down to whole surface pixels at the declared scale and
    // to what the surface holds, while the binding axis stays the camera's.
    private static Vector2 QuantisedSpan(
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

    // The whole surface pixels the fit's grown axis covers, null on an axis it did not grow. The grown
    // axis's surface extent and its quantised span both come from this count, so they cannot disagree.
    // Where the scale comes from the other axis, the camera's size cancels and the count is the
    // binding canvas extent times the output's ratio. Where the canvas holds the grown axis tighter,
    // it is the floor of the exact fraction.
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
                : FloorRatio(size.Y, canvas.Width, outputWidth, size.X, outputHeight);

            return (count, null);
        }

        if (span.Y > size.Y)
        {
            int count = widthBinds
                ? (int)((long)canvas.Width * outputHeight / outputWidth)
                : FloorRatio(size.X, canvas.Height, outputHeight, size.Y, outputWidth);

            return (null, count);
        }

        return (null, null);
    }

    // The floor of (numerator * a * b) / (denominator * c) over whole numbers. A float is an integer
    // mantissa times a power of two, so the quotient is a ratio of integers. In double, a count that
    // is truly whole can land an ulp under it and cost the grown axis a pixel.
    private static int FloorRatio(float numerator, int a, int b, float denominator, int c)
    {
        (Int128 top, int topScale) = Dyadic(numerator);
        (Int128 bottom, int bottomScale) = Dyadic(denominator);
        top *= (long)a * b;
        bottom *= c;

        int shift = topScale - bottomScale;
        if (shift > 0)
        {
            top <<= shift;
        }
        else
        {
            bottom <<= -shift;
        }

        return (int)(top / bottom);
    }

    // value as a whole mantissa and the power of two it scales by. The caller guarantees it is
    // positive and finite.
    private static (Int128 Mantissa, int Scale) Dyadic(float value)
    {
        int bits = BitConverter.SingleToInt32Bits(value);
        int exponent = (bits >> 23) & 0xFF;
        int mantissa = bits & 0x7FFFFF;

        return exponent == 0 ? (mantissa, -149) : (mantissa | 0x800000, exponent - 150);
    }

    // Where the world lands on the render surface. Under Letterbox the declared span is fitted into
    // the surface at whatever scale it allows, and the slack becomes bars. Under Expand or
    // FixedHeight the quantised span is a whole number of surface pixels at the declared pixels per
    // unit, so it is centred at that stated scale, with bars where the surface is larger. A scale
    // recomputed from a division could land an ulp off.
    private static Letterbox WorldFit(in CameraView camera, Vector2 span, float pixelsPerUnit, (int Width, int Height) surface) =>
        camera.Fit == ViewportFit.Letterbox || !(pixelsPerUnit > 0f)
            ? Letterbox.Fit(span.X, span.Y, surface.Width, surface.Height)
            : Letterbox.FitAt(span.X, span.Y, surface.Width, surface.Height, pixelsPerUnit);

    // Half of what the surface has over the canvas, in whole surface pixels. Under Expand or
    // FixedHeight the surface grows past the canvas to reveal more world, and the screen layer stays
    // the canvas, centred in it.
    internal static Vector2 ScreenSlack(int surfaceWidth, int surfaceHeight, Vector2 canvas) => new(
        MathF.Max(MathF.Floor((surfaceWidth - canvas.X) / 2f), 0f),
        MathF.Max(MathF.Floor((surfaceHeight - canvas.Y) / 2f), 0f));

    // Where the canvas lands straight in the back buffer, with no render surface between them. Its
    // own centred fit keeps the layer's aspect and place whatever the camera's fit did with the world
    // behind it. A scale of 0 means a canvas or a window with no area.
    internal static ScreenPlacement WindowPlacement(Vector2 canvas, int outputWidth, int outputHeight)
    {
        Letterbox fit = Letterbox.Fit(canvas.X, canvas.Y, outputWidth, outputHeight);

        return fit.IsEmpty ? default : new ScreenPlacement(new Vector2(fit.X, fit.Y), fit.Scale);
    }

    // Where the render surface's own top-left corner lands in the back buffer. The surface fills the
    // back buffer on its binding axis at the fractional scale that keeps its aspect, with bars on the
    // other axis. A scale of 0 is a surface or a back buffer with no area.
    internal static ScreenPlacement TargetPlacement(int targetWidth, int targetHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.Fit(targetWidth, targetHeight, containerWidth, containerHeight);
        if (fit.IsEmpty)
        {
            return default;
        }

        // The fit's whole-pixel corner, not the exact centre. A bar of an odd number of pixels
        // centres on a half pixel, which puts every texel boundary of a point-sampled blit on a pixel
        // centre and leaves the fill rule to break a tie per row.
        //
        // The scale travels as one scalar. A destination rectangle would round its two extents
        // independently and skew the blit.
        return new ScreenPlacement(new Vector2(fit.X, fit.Y), fit.Scale);
    }

    // The surface a declared canvas draws on for a resolved world rect. Pixels per world unit come
    // from the canvas over the camera's declared span on the fit's binding axis, so the fit changes
    // how much world is on the surface and not how large a world unit is. Under Letterbox the
    // resolved rect is that declared span, so the surface matches the canvas. The surface never
    // shrinks below the canvas and never exceeds the back buffer on an axis, since the present would
    // only scale the extra pixels back down after paying to draw them.
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

    // Surface pixels per world unit: the canvas over the camera's declared span on the axis its fit
    // binds. Under FixedHeight that is the height, which is exact. Otherwise it is whichever axis the
    // canvas holds tighter, the scale Letterbox draws at and Expand keeps. Zero for a span with no
    // area.
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

    // How a surface reaches the back buffer at a present scale. Prescale is the whole factor the
    // surface is first blown up by with point sampling, 1 for none, and Final the sampling of the blit
    // into the back buffer. A point-sampled surface at a fractional scale of 1 or more is prescaled to
    // the next whole scale and filtered down from there. Every texel then keeps an even, crisp block,
    // where a direct point blit would give texels of uneven width and a direct linear blit would blur.
    // Below a scale of 1 there is no block to keep, and the surface is filtered straight down.
    internal static (int Prescale, TextureSampling Final) PresentPass(TextureSampling sampling, float scale)
    {
        if (sampling != TextureSampling.Point)
        {
            return (1, sampling);
        }

        if (scale < 1f)
        {
            return (1, TextureSampling.Linear);
        }

        float whole = MathF.Ceiling(scale);

        return whole == scale ? (1, TextureSampling.Point) : ((int)whole, TextureSampling.Linear);
    }

    // The present pass for a surface drawn factor times larger than a layout at scale declares. The
    // prescale shrinks by the factor and Scale is the final blit's. The blit's source extent and scale
    // are the factor of 1's, so the world lands on the same back-buffer pixels.
    internal static (int Prescale, TextureSampling Final, float Scale) PresentPass(TextureSampling sampling, float scale, int factor)
    {
        (int prescale, TextureSampling final) = PresentPass(sampling, scale);
        int remaining = Math.Max(prescale / factor, 1);

        return (remaining, final, scale / (remaining * factor));
    }

    // The largest factor a surface presented at scale may be drawn larger by: the whole scale a
    // point-sampled surface is presented at or prescaled to. 1 for a linear surface or one shrunk to
    // fit. A factor dividing it keeps the present whole.
    internal static int SurfaceFactorLimit(TextureSampling sampling, float scale) =>
        sampling == TextureSampling.Point && scale >= 1f ? (int)MathF.Ceiling(scale) : 1;

    // The smallest factor dividing limit that is at least ratio, or limit itself past it.
    internal static int SurfaceFactor(float ratio, int limit)
    {
        for (int factor = 1; factor < limit; factor++)
        {
            if (limit % factor == 0 && factor >= ratio)
            {
                return factor;
            }
        }

        return Math.Max(limit, 1);
    }

    // layout on a surface factor times larger. The world and an on-surface screen layer grow with it,
    // and the present scale shrinks by it. Span and Layer are unchanged.
    internal static ScreenLayout Enlarge(in ScreenLayout layout, int factor)
    {
        if (factor == 1)
        {
            return layout;
        }

        Letterbox world = layout.World;

        return layout with
        {
            Surface = (layout.Surface.Width * factor, layout.Surface.Height * factor),
            World = new Letterbox(world.X * factor, world.Y * factor, world.Width * factor, world.Height * factor, world.Scale * factor),
            OnSurface = new ScreenPlacement(layout.OnSurface.Origin * factor, layout.OnSurface.Scale * factor),
            Present = new ScreenPlacement(layout.Present.Origin, layout.Present.Scale / factor),
        };
    }

    // GrownPixels' count on an axis the fit grew, so the surface matches the quantised span there and
    // no extra pixel shows as a one-pixel bar. The clamp's floor absorbs a binding axis whose float
    // product lands under the canvas it equals.
    private static int Extent(int pixels, int canvas, int output) =>
        Math.Clamp(pixels, canvas, Math.Max(canvas, output));
}

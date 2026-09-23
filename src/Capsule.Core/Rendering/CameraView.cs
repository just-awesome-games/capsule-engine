using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// A world-space viewport. The renderer interpolates its centres, sizes and offsets and resolves the
/// size against the output's shape per <see cref="Fit"/>, keeping the scale isotropic on both axes and
/// letterboxing the slack. A non-positive size draws nothing.
/// </summary>
/// <param name="PreviousCenter">The centre as of the previous fixed step.</param>
/// <param name="Center">The centre as of the current fixed step.</param>
/// <param name="Size">World units the viewport spans as of the current fixed step, read per <see cref="Fit"/>.</param>
/// <param name="Fit">How <see cref="Size"/> answers an output of a different aspect ratio.</param>
/// <param name="Bounds">The world rect the visible region is confined to, or null to leave it free.</param>
/// <param name="ScrollOrigin">
/// The top-left corner a scroll factor moves nothing at. An entity with a factor of <c>f</c> is
/// drawn as if by this view with its corner at <c>ScrollOrigin + (Corner - ScrollOrigin) * f</c>,
/// where the corner is that of the world rect the frame places.
/// </param>
public readonly record struct CameraView(
    Vector2 PreviousCenter,
    Vector2 Center,
    Vector2 Size,
    ViewportFit Fit = ViewportFit.Letterbox,
    Rect? Bounds = null,
    Vector2 ScrollOrigin = default)
{
    // How far from square an output may be before a fit that follows its aspect reveals world this view
    // culled. A step's render intent is drawn on whatever output the frame has by then, so a fit other
    // than Letterbox is culled against this ceiling rather than any one output.
    private const float CullAspectCeiling = 4f;

    /// <summary>A view that does not interpolate, with the previous and current centre at one point.</summary>
    public CameraView(Vector2 center, Vector2 size)
        : this(center, center, size)
    {
    }

    /// <summary><see cref="Size"/> as of the previous fixed step, which defaults to <see cref="Size"/>.</summary>
    public Vector2 PreviousSize { get; init; } = Size;

    /// <summary>
    /// How far the view is moved after <see cref="Bounds"/> confine it, in world units, as of the previous
    /// fixed step.
    /// </summary>
    public Vector2 PreviousOffset { get; init; }

    /// <summary>
    /// How far the view is moved after <see cref="Bounds"/> confine it, in world units, as of the current
    /// fixed step.
    /// </summary>
    public Vector2 Offset { get; init; }

    /// <summary>
    /// The union of the viewport regions this view resolves to across a step, which culling tests
    /// against. <see cref="Bounds"/> confine it as they confine a drawn frame, and a
    /// <see cref="Fit"/> other than <see cref="ViewportFit.Letterbox"/> widens it to cover any
    /// output up to four times as wide as it is tall, or as tall as it is wide.
    /// </summary>
    public Rect SweptBounds
    {
        get
        {
            Vector2 halfSize = CullSpan() / 2f;
            Vector2 low = Vector2.Min(PreviousCenter, Center);
            Vector2 high = Vector2.Max(PreviousCenter, Center);

            // Confining is a clamp, so it is monotone in the centre. Confining the two endpoints covers
            // every centre the frame interpolates between them, and confining at the widest span covers
            // every narrower span an output or an interpolated size could ask for.
            if (Bounds is { } bounds)
            {
                low = Confine(low, halfSize, bounds);
                high = Confine(high, halfSize, bounds);
            }

            return Rect.Sweep(
                low + Vector2.Min(PreviousOffset, Offset),
                high + Vector2.Max(PreviousOffset, Offset),
                halfSize);
        }
    }

    /// <summary>
    /// The world rect this view shows on an output of <paramref name="outputSize"/> pixels. The centre,
    /// size and offset are interpolated by <paramref name="alpha"/>. The size is resolved against the
    /// output's aspect per <see cref="Fit"/>, the rect is confined to <see cref="Bounds"/>, and the
    /// offset moves it last.
    /// </summary>
    /// <param name="alpha">Fraction of a fixed step not yet simulated, in [0, 1]. 0 draws the previous step and 1 the current one.</param>
    /// <param name="outputSize">
    /// The output's extent in pixels. Only its aspect ratio is read, and an extent with no area on
    /// either axis falls back to <see cref="ViewportFit.Letterbox"/>, which needs none.
    /// </param>
    /// <returns>The visible world rect. Empty when <see cref="Size"/> is not positive on both axes.</returns>
    public Rect Resolve(float alpha, Vector2 outputSize)
    {
        CameraView still = At(alpha);

        return still.Place(1f, still.ResolveSpan(outputSize));
    }

    // This view as drawn at alpha, with nothing left to interpolate. A frame is laid out against it and
    // resolves the interpolated size.
    internal CameraView At(float alpha)
    {
        Vector2 center = StepInterpolation.Interpolate(PreviousCenter, Center, alpha);
        Vector2 size = StepInterpolation.Interpolate(PreviousSize, Size, alpha);
        Vector2 offset = StepInterpolation.Interpolate(PreviousOffset, Offset, alpha);

        return this with
        {
            PreviousCenter = center,
            Center = center,
            PreviousSize = size,
            Size = size,
            PreviousOffset = offset,
            Offset = offset,
        };
    }

    // The world rect this view shows across span world units, with the centre and offset interpolated
    // by alpha, confined to Bounds and then offset as Resolve places it. The renderer calls this with
    // ResolveSpan's span quantised down to whole surface pixels, so what it draws agrees with this rect
    // and the placed span stays within what SweptBounds covers.
    internal Rect Place(float alpha, Vector2 span)
    {
        // Negated comparisons reject a NaN span along with the non-positive ones.
        if (!(Size.X > 0f) || !(Size.Y > 0f))
        {
            return default;
        }

        Vector2 half = span / 2f;
        Vector2 center = StepInterpolation.Interpolate(PreviousCenter, Center, alpha);

        if (Bounds is { } bounds)
        {
            center = Confine(center, half, bounds);
        }

        center += StepInterpolation.Interpolate(PreviousOffset, Offset, alpha);

        return new Rect(center.X - half.X, center.Y - half.Y, center.X + half.X, center.Y + half.Y);
    }

    /// <summary>
    /// The world units this view spans on an output of <paramref name="outputSize"/> pixels, per
    /// <see cref="Fit"/>. This is the span <see cref="Resolve"/> places at an alpha of 1. A caller that
    /// needs both takes the span from here, because subtracting the resolved rect's edges loses
    /// precision far from the origin.
    /// </summary>
    /// <param name="outputSize">
    /// The output's extent in pixels. Only its aspect ratio is read, and an extent with no area on
    /// either axis resolves <see cref="Size"/> unchanged.
    /// </param>
    /// <returns><see cref="Size"/> unchanged under <see cref="ViewportFit.Letterbox"/>.</returns>
    public Vector2 ResolveSpan(Vector2 outputSize)
    {
        if (Fit == ViewportFit.Letterbox || !(outputSize.X > 0f) || !(outputSize.Y > 0f))
        {
            return Size;
        }

        float aspect = outputSize.X / outputSize.Y;

        if (Fit == ViewportFit.FixedHeight)
        {
            return new Vector2(Size.Y * aspect, Size.Y);
        }

        // Expand grows whichever axis the output has slack on and holds the other at its declared
        // minimum, so the scale matches what the binding axis would have given.
        return aspect > Size.X / Size.Y
            ? new Vector2(Size.Y * aspect, Size.Y)
            : new Vector2(Size.X, Size.X / aspect);
    }

    // The view an entity with this scroll factor is drawn by, shaped so its SweptBounds cover every rect
    // the frame can draw that entity's layer at. A frame places the real view at a span s' no wider than
    // the cull span s, with its centre within (s - s') / 2 of the endpoints confined at s, so the
    // placed rect's corner K lies in [Kmin, Kmax - s'] where [Kmin, Kmax] is SweptBounds. The layer's
    // rect is [O + (K - O) f, O + (K - O) f + s']. For f >= 0 that lies within the map of
    // [Kmin, Kmax - t s] widened by s on the far side, tightly at t = min(1, 1/f). A negative f reverses
    // the map, so t = 0 and the far-side widening grows to (1 - f) s.
    internal CameraView ScrolledBy(Vector2 factor)
    {
        Rect swept = SweptBounds;
        Vector2 span = CullSpan();
        Vector2 near = new(swept.Left, swept.Top);
        Vector2 far = new(swept.Right - (Reach(factor.X) * span.X), swept.Bottom - (Reach(factor.Y) * span.Y));

        float widen = MathF.Max(1f, MathF.Max(1f - factor.X, 1f - factor.Y));
        Vector2 half = span * widen / 2f;

        return new CameraView(
            ScrollOrigin + ((near - ScrollOrigin) * factor) + half,
            ScrollOrigin + ((far - ScrollOrigin) * factor) + half,
            CullSize() * widen,
            Fit,
            Bounds: null,
            ScrollOrigin);
    }

    // How much of the cull span the far endpoint's corner is pulled back before the map. It is the full
    // span up to a factor of one, and 1/f of it past that.
    private static float Reach(float factor) => factor >= 0f ? MathF.Min(1f, 1f / factor) : 0f;

    // The larger of the two sizes on each axis, which covers every size interpolated between them.
    private Vector2 CullSize() => Vector2.Max(PreviousSize, Size);

    private Vector2 CullSpan()
    {
        Vector2 size = CullSize();

        return Fit switch
        {
            ViewportFit.FixedHeight => new Vector2(MathF.Max(size.X, size.Y * CullAspectCeiling), size.Y),
            ViewportFit.Expand => new Vector2(
                MathF.Max(size.X, size.Y * CullAspectCeiling),
                MathF.Max(size.Y, size.X * CullAspectCeiling)),
            _ => size,
        };
    }

    private static Vector2 Confine(Vector2 center, Vector2 half, in Rect bounds) => new(
        Confine(center.X, half.X, bounds.Left, bounds.Right),
        Confine(center.Y, half.Y, bounds.Top, bounds.Bottom));

    // A span the bounds cannot hold is centred on them. Clamping it would pin one edge and show world
    // past the other.
    private static float Confine(float center, float half, float low, float high) =>
        high - low <= half * 2f
            ? (low + high) / 2f
            : Math.Clamp(center, low + half, high - half);
}

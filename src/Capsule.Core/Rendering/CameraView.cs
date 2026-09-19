using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// A world-space viewport. The renderer interpolates its centres and resolves <see cref="Size"/>
/// against the output's shape per <see cref="Fit"/>, keeping the scale isotropic on both axes and
/// letterboxing the slack. A non-positive size draws nothing.
/// </summary>
/// <param name="PreviousCenter">The centre as of the previous fixed step.</param>
/// <param name="Center">The centre as of the current fixed step.</param>
/// <param name="Size">World units the viewport spans, read per <see cref="Fit"/>.</param>
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
    // culled. The output never reaches the simulation, so culling has no window to measure and a fit
    // other than Letterbox is culled against this ceiling.
    private const float CullAspectCeiling = 4f;

    /// <summary>A view that does not interpolate, with the previous and current centre at one point.</summary>
    public CameraView(Vector2 center, Vector2 size)
        : this(center, center, size)
    {
    }

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

            if (Bounds is not { } bounds)
            {
                return Rect.Sweep(PreviousCenter, Center, halfSize);
            }

            // Confining is a clamp, so it is monotone in the centre. Confining the two endpoints covers
            // every centre the frame interpolates between them, and confining at the widest span covers
            // every narrower span an output could ask for.
            Vector2 low = new(
                Confine(MathF.Min(PreviousCenter.X, Center.X), halfSize.X, bounds.Left, bounds.Right),
                Confine(MathF.Min(PreviousCenter.Y, Center.Y), halfSize.Y, bounds.Top, bounds.Bottom));
            Vector2 high = new(
                Confine(MathF.Max(PreviousCenter.X, Center.X), halfSize.X, bounds.Left, bounds.Right),
                Confine(MathF.Max(PreviousCenter.Y, Center.Y), halfSize.Y, bounds.Top, bounds.Bottom));

            return Rect.Sweep(low, high, halfSize);
        }
    }

    /// <summary>
    /// The world rect this view shows on an output of <paramref name="outputSize"/> pixels. The centre
    /// is interpolated by <paramref name="alpha"/>, <see cref="Size"/> is resolved against the
    /// output's aspect per <see cref="Fit"/>, and the result is confined to <see cref="Bounds"/>.
    /// Nothing here reaches the simulation, so the output does not change what a run computes.
    /// </summary>
    /// <param name="alpha">Fraction of a fixed step not yet simulated, in [0, 1]. 0 draws the previous centre and 1 the current one.</param>
    /// <param name="outputSize">
    /// The output's extent in pixels. Only its aspect ratio is read, and an extent with no area on
    /// either axis falls back to <see cref="ViewportFit.Letterbox"/>, which needs none.
    /// </param>
    /// <returns>The visible world rect. Empty when <see cref="Size"/> is not positive on both axes.</returns>
    public Rect Resolve(float alpha, Vector2 outputSize) => Place(alpha, ResolveSpan(outputSize));

    // The world rect this view shows across span world units, with the centre interpolated by alpha and
    // the rect confined to Bounds as Resolve confines it. The renderer calls this with ResolveSpan's span
    // quantised down to whole surface pixels, so what it draws agrees with this rect and the placed span
    // stays within what SweptBounds covers.
    internal Rect Place(float alpha, Vector2 span)
    {
        // Negated comparisons reject a NaN span along with the non-positive ones.
        if (!(Size.X > 0f) || !(Size.Y > 0f))
        {
            return default;
        }

        Vector2 center = StepInterpolation.Interpolate(PreviousCenter, Center, alpha);

        Vector2 half = span / 2f;

        if (Bounds is not { } bounds)
        {
            return new Rect(center.X - half.X, center.Y - half.Y, center.X + half.X, center.Y + half.Y);
        }

        float x = Confine(center.X, half.X, bounds.Left, bounds.Right);
        float y = Confine(center.Y, half.Y, bounds.Top, bounds.Bottom);

        return new Rect(x - half.X, y - half.Y, x + half.X, y + half.Y);
    }

    /// <summary>
    /// The world units this view spans on an output of <paramref name="outputSize"/> pixels, per
    /// <see cref="Fit"/>. This is the span <see cref="Resolve"/> places. A caller that needs both
    /// takes the span from here, because subtracting the resolved rect's edges loses precision far
    /// from the origin.
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
    // the frame can draw that entity's layer at. A frame places the real view at a span s' between Size
    // and the cull span s, with its centre within (s - s') / 2 of the endpoints confined at s, so the
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
            Size * widen,
            Fit,
            Bounds: null,
            ScrollOrigin);
    }

    // How much of the cull span the far endpoint's corner is pulled back before the map. It is the full
    // span up to a factor of one, and 1/f of it past that.
    private static float Reach(float factor) => factor >= 0f ? MathF.Min(1f, 1f / factor) : 0f;

    private Vector2 CullSpan() => Fit switch
    {
        ViewportFit.FixedHeight => new Vector2(MathF.Max(Size.X, Size.Y * CullAspectCeiling), Size.Y),
        ViewportFit.Expand => new Vector2(
            MathF.Max(Size.X, Size.Y * CullAspectCeiling),
            MathF.Max(Size.Y, Size.X * CullAspectCeiling)),
        _ => Size,
    };

    // A span the bounds cannot hold is centred on them. Clamping it would pin one edge and show world
    // past the other.
    private static float Confine(float center, float half, float low, float high) =>
        high - low <= half * 2f
            ? (low + high) / 2f
            : Math.Clamp(center, low + half, high - half);
}

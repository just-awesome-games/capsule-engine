using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One straight segment of flat colour as the simulation wants it drawn, from <see cref="A"/> to
/// <see cref="B"/>. Drawn where it is, at the settled step: a line does not interpolate the way a
/// <see cref="SpriteIntent"/> does, and its ends quantise to the frame's pixel grid when the
/// frame's sprites do. A segment of no length draws nothing.
/// </summary>
/// <param name="A">One end, in the drawn space's units.</param>
/// <param name="B">The other end, in the drawn space's units.</param>
/// <param name="Thickness">
/// How wide the segment is drawn, in the drawn space's units, centred on the segment. Zero — the
/// default — draws one pixel of the surface the frame is rasterised on: the window, or the declared
/// internal-resolution surface, whose pixels the present scales with the rest of the frame. A
/// negative or non-finite thickness draws nothing.
/// </param>
/// <param name="Color">The colour filled, straight alpha.</param>
public readonly record struct LineIntent(Vector2 A, Vector2 B, float Thickness, ColorRgba Color)
{
    /// <summary>A hairline segment: <see cref="Thickness"/> zero.</summary>
    public LineIntent(Vector2 a, Vector2 b, ColorRgba color)
        : this(a, b, 0f, color)
    {
    }

    // The rect the drawn segment covers, or false where it draws nothing: an end or a thickness
    // that is not finite, a negative thickness, or no length. The rect can have no area on an
    // axis — an axis-aligned hairline — and still be a region something is drawn in, so
    // Rect.IsEmpty is not the test here.
    internal bool TryGetBounds(out Rect bounds)
    {
        bounds = default;

        if (!float.IsFinite(A.X) || !float.IsFinite(A.Y) || !float.IsFinite(B.X) || !float.IsFinite(B.Y)
            || !(Thickness >= 0f) || !float.IsFinite(Thickness) || A == B)
        {
            return false;
        }

        float half = Thickness / 2f;
        bounds = new Rect(
            MathF.Min(A.X, B.X) - half,
            MathF.Min(A.Y, B.Y) - half,
            MathF.Max(A.X, B.X) + half,
            MathF.Max(A.Y, B.Y) + half);

        return true;
    }
}

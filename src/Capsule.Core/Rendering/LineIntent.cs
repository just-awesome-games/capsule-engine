using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One straight segment of flat colour as the simulation wants it drawn, from <see cref="A"/> to
/// <see cref="B"/>. It is drawn at the settled step and does not interpolate the way a
/// <see cref="SpriteIntent"/> does.
/// </summary>
/// <remarks>
/// Its ends quantise to the frame's pixel grid when the frame's sprites do. A segment of no length
/// draws nothing.
/// </remarks>
/// <param name="A">One end, in the drawn space's units.</param>
/// <param name="B">The other end, in the drawn space's units.</param>
/// <param name="Thickness">
/// How wide the segment is drawn, in the drawn space's units, centred on the segment. Zero draws one
/// pixel of the surface the frame is rasterised on. A negative or non-finite
/// thickness draws nothing.
/// </param>
/// <param name="Color">The colour filled, with straight alpha.</param>
public readonly record struct LineIntent(Vector2 A, Vector2 B, float Thickness, ColorRgba Color)
{
    /// <summary>A hairline segment, with <see cref="Thickness"/> at zero.</summary>
    public LineIntent(Vector2 a, Vector2 b, ColorRgba color)
        : this(a, b, 0f, color)
    {
    }

    // The rect the drawn segment covers, or false where it draws nothing: a non-finite end or thickness, a
    // negative thickness, or no length. An axis-aligned hairline has no area on one axis and is still a
    // region something is drawn in, so Rect.IsEmpty is not the test here.
    internal bool TryGetBounds(out Rect bounds)
    {
        bounds = default;

        if (!float.IsFinite(A.X) || !float.IsFinite(A.Y) || !float.IsFinite(B.X) || !float.IsFinite(B.Y)
            || !(Thickness >= 0f) || !float.IsFinite(Thickness) || A == B)
        {
            return false;
        }

        float half = Thickness / 2f;
        bounds = Rect.Sweep(A, B, new Vector2(half, half));

        return true;
    }
}

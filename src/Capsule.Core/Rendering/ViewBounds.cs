namespace Capsule.Rendering;

/// <summary>
/// An axis-aligned world rect. World units, Y-down: <see cref="Left"/> and <see cref="Top"/> are
/// the low edges, <see cref="Right"/> and <see cref="Bottom"/> the high ones. It encloses an
/// open region, so two rects sharing an edge do not intersect.
/// </summary>
public readonly record struct ViewBounds(float Left, float Top, float Right, float Bottom)
{
    /// <summary>
    /// Whether this rect encloses nothing testable — no area on an axis, or an edge that is not
    /// finite. NaN lands here too, since it compares false to everything.
    /// </summary>
    public bool IsEmpty =>
        !(Right > Left) ||
        !(Bottom > Top) ||
        !float.IsFinite(Left) ||
        !float.IsFinite(Top) ||
        !float.IsFinite(Right) ||
        !float.IsFinite(Bottom);

    /// <summary>Whether this rect and <paramref name="other"/> overlap on both axes.</summary>
    public bool Intersects(in ViewBounds other) =>
        Right > other.Left &&
        Bottom > other.Top &&
        Left < other.Right &&
        Top < other.Bottom;
}

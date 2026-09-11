using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// An axis-aligned world rect, given as its four edges rather than a corner and an extent. World
/// units, Y-down: <see cref="Left"/> and <see cref="Top"/> are the low edges. It encloses an open
/// region, so two rects sharing an edge do not intersect.
/// </summary>
/// <param name="Left">The low edge on X.</param>
/// <param name="Top">The low edge on Y, which is Y-down and so the upper one on screen.</param>
/// <param name="Right">The high edge on X, not a width.</param>
/// <param name="Bottom">The high edge on Y, not a height.</param>
public readonly record struct ViewBounds(float Left, float Top, float Right, float Bottom)
{
    /// <summary>
    /// Whether this rect encloses nothing testable: no area on an axis, or a non-finite edge. NaN
    /// lands here too, since it compares false to everything.
    /// </summary>
    public bool IsEmpty =>
        !(Right > Left) ||
        !(Bottom > Top) ||
        !float.IsFinite(Left) ||
        !float.IsFinite(Top) ||
        !float.IsFinite(Right) ||
        !float.IsFinite(Bottom);

    /// <summary>
    /// Whether <paramref name="point"/> lies inside this rect, in the same units. The region is open,
    /// so a point on any edge is outside it and two rects sharing an edge never both claim a point on
    /// it; an empty rect claims none at all, and a non-finite coordinate lands outside everything.
    /// </summary>
    public bool Contains(Vector2 point) =>
        !IsEmpty &&
        point.X > Left &&
        point.X < Right &&
        point.Y > Top &&
        point.Y < Bottom;

    /// <summary>Whether this rect and <paramref name="other"/> overlap on both axes.</summary>
    public bool Intersects(in ViewBounds other) =>
        Right > other.Left &&
        Bottom > other.Top &&
        Left < other.Right &&
        Top < other.Bottom;
}

using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// An axis-aligned rect, held as its four edges rather than a corner and an extent, in whatever
/// units the thing reporting it names — world units or canvas pixels. Y-down:
/// <see cref="Left"/> and <see cref="Top"/> are the low edges. It encloses an open region, so two
/// rects sharing an edge do not intersect.
/// </summary>
/// <param name="Left">The low edge on X.</param>
/// <param name="Top">The low edge on Y, which is Y-down and so the upper one on screen.</param>
/// <param name="Right">The high edge on X, not a width.</param>
/// <param name="Bottom">The high edge on Y, not a height.</param>
public readonly record struct Rect(float Left, float Top, float Right, float Bottom)
{
    /// <summary>The rect spanning <paramref name="size"/> from the corner at <paramref name="position"/>.</summary>
    /// <param name="position">The top-left corner, which is the low edge on both axes.</param>
    /// <param name="size">The extent spanned from that corner; a non-positive axis encloses nothing.</param>
    public Rect(Vector2 position, Vector2 size)
        : this(position.X, position.Y, position.X + size.X, position.Y + size.Y)
    {
    }

    /// <summary>The top-left corner: the low edge on each axis.</summary>
    public Vector2 Position => new(Left, Top);

    /// <summary>The extent between the edges on each axis, which is negative where they are crossed.</summary>
    public Vector2 Size => new(Right - Left, Bottom - Top);

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
    public bool Intersects(in Rect other) =>
        Right > other.Left &&
        Bottom > other.Top &&
        Left < other.Right &&
        Top < other.Bottom;
}

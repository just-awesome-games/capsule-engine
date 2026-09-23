using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// An axis-aligned rect, held as its four edges instead of a corner and an extent, in whatever
/// units the thing reporting it names.
/// </summary>
/// <remarks>
/// The plane is Y-down, and <see cref="Left"/> and <see cref="Top"/> are the low edges. Overlap is
/// open, and two rects that only share an edge do not <see cref="Intersects"/>. Containment is
/// half-open.
/// </remarks>
/// <param name="Left">The low edge on X.</param>
/// <param name="Top">The low edge on Y, the upper one on screen.</param>
/// <param name="Right">The high edge on X, not a width.</param>
/// <param name="Bottom">The high edge on Y, not a height.</param>
public readonly record struct Rect(float Left, float Top, float Right, float Bottom)
{
    /// <summary>The rect spanning <paramref name="size"/> from the corner at <paramref name="position"/>.</summary>
    /// <param name="position">The top-left corner, which is the low edge on both axes.</param>
    /// <param name="size">The extent spanned from that corner. A non-positive axis encloses nothing.</param>
    public Rect(Vector2 position, Vector2 size)
        : this(position.X, position.Y, position.X + size.X, position.Y + size.Y)
    {
    }

    /// <summary>The top-left corner, which is the low edge on each axis.</summary>
    public Vector2 Position => new(Left, Top);

    /// <summary>The extent between the edges on each axis, which is negative where they are crossed.</summary>
    public Vector2 Size => new(Right - Left, Bottom - Top);

    /// <summary>
    /// Whether this rect encloses nothing testable, meaning no area on an axis or a non-finite edge,
    /// NaN included.
    /// </summary>
    public bool IsEmpty =>
        !(Right > Left) ||
        !(Bottom > Top) ||
        !float.IsFinite(Left) ||
        !float.IsFinite(Top) ||
        !float.IsFinite(Right) ||
        !float.IsFinite(Bottom);

    /// <summary>
    /// Whether <paramref name="point"/> lies inside this rect, in the same units.
    /// </summary>
    /// <remarks>
    /// The region is half-open, with <see cref="Left"/> and <see cref="Top"/> inside and
    /// <see cref="Right"/> and <see cref="Bottom"/> outside. Abutting rects tile the plane with no
    /// point claimed twice. An empty rect claims nothing, and a non-finite coordinate lands outside
    /// every rect.
    /// </remarks>
    public bool Contains(Vector2 point) =>
        !IsEmpty &&
        point.X >= Left &&
        point.X < Right &&
        point.Y >= Top &&
        point.Y < Bottom;

    /// <summary>Whether this rect and <paramref name="other"/> overlap on both axes.</summary>
    public bool Intersects(in Rect other) =>
        Right > other.Left &&
        Bottom > other.Top &&
        Left < other.Right &&
        Top < other.Bottom;

    // The rect covering a box swept between two points. The box reaches lowPad from a point towards the
    // low edges and highPad towards the high ones, both measured from the point.
    internal static Rect Sweep(Vector2 from, Vector2 to, Vector2 lowPad, Vector2 highPad) =>
        new(
            MathF.Min(from.X, to.X) + lowPad.X,
            MathF.Min(from.Y, to.Y) + lowPad.Y,
            MathF.Max(from.X, to.X) + highPad.X,
            MathF.Max(from.Y, to.Y) + highPad.Y);

    // The same sweep where the box is centred on the point.
    internal static Rect Sweep(Vector2 from, Vector2 to, Vector2 reach) => Sweep(from, to, -reach, reach);
}

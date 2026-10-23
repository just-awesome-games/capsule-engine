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
public readonly record struct Rect
{
    /// <summary>The low edge on X.</summary>
    public float Left { get; init; }

    /// <summary>The low edge on Y, the upper one on screen.</summary>
    public float Top { get; init; }

    /// <summary>The high edge on X, not a width.</summary>
    public float Right { get; init; }

    /// <summary>The high edge on Y, not a height.</summary>
    public float Bottom { get; init; }

    /// <summary>The rect spanning <paramref name="size"/> from the corner at <paramref name="position"/>.</summary>
    /// <param name="position">The top-left corner, which is the low edge on both axes.</param>
    /// <param name="size">The extent spanned from that corner. A non-positive axis encloses nothing.</param>
    public Rect(Vector2 position, Vector2 size)
    {
        Left = position.X;
        Top = position.Y;
        Right = position.X + size.X;
        Bottom = position.Y + size.Y;
    }

    /// <summary>The rect spanning <paramref name="width"/> and <paramref name="height"/> from the corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="x">The low edge on X.</param>
    /// <param name="y">The low edge on Y, the upper one on screen.</param>
    /// <param name="width">The extent spanned on X. A non-positive width encloses nothing.</param>
    /// <param name="height">The extent spanned on Y. A non-positive height encloses nothing.</param>
    public Rect(float x, float y, float width, float height)
        : this(new Vector2(x, y), new Vector2(width, height))
    {
    }

    /// <summary>The top-left corner, which is the low edge on each axis.</summary>
    public Vector2 Position => new(Left, Top);

    /// <summary>The extent between the edges on each axis, which is negative where they are crossed.</summary>
    public Vector2 Size => new(Right - Left, Bottom - Top);

    /// <summary>The point midway between the edges on each axis.</summary>
    public Vector2 Center => new((Left + Right) * 0.5f, (Top + Bottom) * 0.5f);

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

    /// <summary>The rect bounded by four edges, each a coordinate rather than an extent.</summary>
    /// <param name="left">The low edge on X.</param>
    /// <param name="top">The low edge on Y, the upper one on screen.</param>
    /// <param name="right">The high edge on X, not a width.</param>
    /// <param name="bottom">The high edge on Y, not a height.</param>
    public static Rect FromEdges(float left, float top, float right, float bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    /// <summary>Whether this rect and <paramref name="other"/> overlap on both axes.</summary>
    public bool Intersects(in Rect other) =>
        Right > other.Left &&
        Bottom > other.Top &&
        Left < other.Right &&
        Top < other.Bottom;

    // The rect covering a box swept between two points. The box reaches lowPad from a point towards the
    // low edges and highPad towards the high ones, both measured from the point.
    internal static Rect Sweep(Vector2 from, Vector2 to, Vector2 lowPad, Vector2 highPad) =>
        FromEdges(
            MathF.Min(from.X, to.X) + lowPad.X,
            MathF.Min(from.Y, to.Y) + lowPad.Y,
            MathF.Max(from.X, to.X) + highPad.X,
            MathF.Max(from.Y, to.Y) + highPad.Y);

    // The same sweep where the box is centred on the point.
    internal static Rect Sweep(Vector2 from, Vector2 to, Vector2 reach) => Sweep(from, to, -reach, reach);

    // The rect both cover, empty where they do not overlap.
    internal static Rect Intersection(in Rect first, in Rect second) =>
        FromEdges(
            MathF.Max(first.Left, second.Left),
            MathF.Max(first.Top, second.Top),
            MathF.Min(first.Right, second.Right),
            MathF.Min(first.Bottom, second.Bottom));

    // The smallest rect covering both.
    internal static Rect Union(in Rect first, in Rect second) =>
        FromEdges(
            MathF.Min(first.Left, second.Left),
            MathF.Min(first.Top, second.Top),
            MathF.Max(first.Right, second.Right),
            MathF.Max(first.Bottom, second.Bottom));
}

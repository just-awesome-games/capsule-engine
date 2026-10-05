using System.Globalization;
using System.Numerics;

namespace Capsule.UI;

/// <summary>
/// Where a <see cref="ScreenEntity"/> sits in its parent's padded rect, or in the canvas for a root, as a
/// fraction of that rect on each axis, Y-down. (0, 0) is the top-left corner and (1, 1) the bottom-right.
/// </summary>
/// <remarks>
/// In the point form, the same point of the entity's own rect sits <see cref="Capsule.Scenes.Entity.Position"/>
/// from the anchor. In the span form, the rect stretches over that share of the parent's rect from the
/// span's near edge. A fraction outside [0, 1] names a point outside the rect and is allowed.
/// </remarks>
public readonly record struct Anchor
{
    private readonly float _minX;
    private readonly float _minY;
    private readonly float _maxX;
    private readonly float _maxY;

    /// <summary>An anchor at one point of the parent's rect.</summary>
    /// <param name="x">The fraction across, where 0 is the left edge and 1 the right.</param>
    /// <param name="y">The fraction down, where 0 is the top edge and 1 the bottom.</param>
    public Anchor(float x, float y)
        : this(x, y, x, y)
    {
    }

    /// <summary>
    /// An anchor spanning a range of the parent's rect on each axis, where equal ends make that axis a point.
    /// </summary>
    /// <param name="minX">The fraction across where the span starts.</param>
    /// <param name="minY">The fraction down where the span starts.</param>
    /// <param name="maxX">The fraction across where the span ends, at least <paramref name="minX"/>.</param>
    /// <param name="maxY">The fraction down where the span ends, at least <paramref name="minY"/>.</param>
    public Anchor(float minX, float minY, float maxX, float maxY)
    {
        Guard.Finite(minX, nameof(minX));
        Guard.Finite(minY, nameof(minY));
        Guard.Finite(maxX, nameof(maxX));
        Guard.Finite(maxY, nameof(maxY));

        if (maxX < minX)
        {
            throw new ArgumentOutOfRangeException(nameof(maxX), maxX, "An anchor's span ends before it starts on X. Pass the smaller fraction as minX.");
        }

        if (maxY < minY)
        {
            throw new ArgumentOutOfRangeException(nameof(maxY), maxY, "An anchor's span ends before it starts on Y. Pass the smaller fraction as minY.");
        }

        _minX = minX;
        _minY = minY;
        _maxX = maxX;
        _maxY = maxY;
    }

    /// <summary>The top-left corner, and the default anchor.</summary>
    public static Anchor TopLeft => default;

    /// <summary>The middle of the top edge.</summary>
    public static Anchor Top => new(0.5f, 0f);

    /// <summary>The top-right corner.</summary>
    public static Anchor TopRight => new(1f, 0f);

    /// <summary>The middle of the left edge.</summary>
    public static Anchor Left => new(0f, 0.5f);

    /// <summary>The middle.</summary>
    public static Anchor Center => new(0.5f, 0.5f);

    /// <summary>The middle of the right edge.</summary>
    public static Anchor Right => new(1f, 0.5f);

    /// <summary>The bottom-left corner.</summary>
    public static Anchor BottomLeft => new(0f, 1f);

    /// <summary>The middle of the bottom edge.</summary>
    public static Anchor Bottom => new(0.5f, 1f);

    /// <summary>The bottom-right corner.</summary>
    public static Anchor BottomRight => new(1f, 1f);

    /// <summary>The whole rect on both axes.</summary>
    public static Anchor Fill => new(0f, 0f, 1f, 1f);

    /// <summary>The full width along the top edge.</summary>
    public static Anchor TopWide => new(0f, 0f, 1f, 0f);

    /// <summary>The full width along the bottom edge.</summary>
    public static Anchor BottomWide => new(0f, 1f, 1f, 1f);

    /// <summary>The full height along the left edge.</summary>
    public static Anchor LeftTall => new(0f, 0f, 0f, 1f);

    /// <summary>The full height along the right edge.</summary>
    public static Anchor RightTall => new(1f, 0f, 1f, 1f);

    /// <summary>The anchor as <c>(x, y)</c> for a point, or <c>(minX, minY)-(maxX, maxY)</c> for a span.</summary>
    public override string ToString() => _minX == _maxX && _minY == _maxY
        ? string.Create(CultureInfo.InvariantCulture, $"({_minX}, {_minY})")
        : string.Create(CultureInfo.InvariantCulture, $"({_minX}, {_minY})-({_maxX}, {_maxY})");

    // Places a rect of `size` inside the rect from `min` spanning `extent`. Returns the anchor point,
    // which is a spanned axis's near edge, and writes the rect's extent.
    internal Vector2 Place(Vector2 min, Vector2 extent, Vector2 size, out Vector2 placed)
    {
        placed = Extent(extent, size);

        return new Vector2(min.X + (_minX * extent.X), min.Y + (_minY * extent.Y));
    }

    // The point of a rect this wide and tall that sits on the anchor point, from its top-left corner. A
    // point axis puts the same fraction of the rect there, and a spanned axis its near edge.
    internal Vector2 Pivot(Vector2 placed) =>
        new(_minX == _maxX ? _minX * placed.X : 0f, _minY == _maxY ? _minY * placed.Y : 0f);

    // The extent alone, for a reader that needs no anchor point.
    internal Vector2 Extent(Vector2 extent, Vector2 size) =>
        new(Extent(_minX, _maxX, extent.X, size.X), Extent(_minY, _maxY, extent.Y, size.Y));

    private static float Extent(float from, float to, float extent, float size) =>
        from == to ? size : (to - from) * extent;
}

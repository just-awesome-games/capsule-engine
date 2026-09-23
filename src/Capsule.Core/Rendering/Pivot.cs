using System.Numerics;

namespace Capsule.Rendering;

/// <summary>A point on a box given as a fraction of its extent on each axis.</summary>
/// <remarks>
/// In the Y-down plane (0, 0) is the box's top-left corner and (1, 1) its bottom-right. A box is
/// placed so the point its pivot names lands on the position it is drawn at. A fraction outside [0,
/// 1] offsets the box by a multiple of its own extent.
/// </remarks>
public readonly record struct Pivot(float X, float Y)
{
    /// <summary>The box's top-left corner, which is the default.</summary>
    public static Pivot TopLeft => default;

    /// <summary>The middle of the box's top edge.</summary>
    public static Pivot Top => new(0.5f, 0f);

    /// <summary>The box's top-right corner.</summary>
    public static Pivot TopRight => new(1f, 0f);

    /// <summary>The middle of the box's left edge.</summary>
    public static Pivot Left => new(0f, 0.5f);

    /// <summary>The middle of the box.</summary>
    public static Pivot Center => new(0.5f, 0.5f);

    /// <summary>The middle of the box's right edge.</summary>
    public static Pivot Right => new(1f, 0.5f);

    /// <summary>The box's bottom-left corner.</summary>
    public static Pivot BottomLeft => new(0f, 1f);

    /// <summary>The middle of the box's bottom edge.</summary>
    public static Pivot Bottom => new(0.5f, 1f);

    /// <summary>The box's bottom-right corner.</summary>
    public static Pivot BottomRight => new(1f, 1f);

    // Where this pivot lands on a box of the given extent, measured from its top-left corner.
    internal Vector2 On(Vector2 box) => new(X * box.X, Y * box.Y);
}

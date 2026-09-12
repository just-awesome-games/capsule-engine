using System.Numerics;

namespace Capsule.Scenes;

/// <summary>
/// A point on the canvas given as a fraction of its extent on each axis, Y-down: (0, 0) is the
/// top-left corner, (1, 1) the bottom-right. A <see cref="ScreenEntity"/>'s position is measured from
/// the point its anchor names, so a corner-anchored interface element keeps its distance from that
/// corner whatever the canvas is.
/// <para>
/// A fraction outside [0, 1] is a point off the canvas, which is allowed: it anchors something just
/// past an edge.
/// </para>
/// </summary>
/// <param name="X">The fraction across the canvas on X; 0 is its left edge and 1 its right.</param>
/// <param name="Y">The fraction down the canvas on Y; 0 is its top edge and 1 its bottom.</param>
public readonly record struct Anchor(float X, float Y)
{
    /// <summary>The canvas's top-left corner, which is the default.</summary>
    public static Anchor TopLeft => default;

    /// <summary>The middle of the canvas's top edge.</summary>
    public static Anchor Top => new(0.5f, 0f);

    /// <summary>The canvas's top-right corner.</summary>
    public static Anchor TopRight => new(1f, 0f);

    /// <summary>The middle of the canvas's left edge.</summary>
    public static Anchor Left => new(0f, 0.5f);

    /// <summary>The middle of the canvas.</summary>
    public static Anchor Center => new(0.5f, 0.5f);

    /// <summary>The middle of the canvas's right edge.</summary>
    public static Anchor Right => new(1f, 0.5f);

    /// <summary>The canvas's bottom-left corner.</summary>
    public static Anchor BottomLeft => new(0f, 1f);

    /// <summary>The middle of the canvas's bottom edge.</summary>
    public static Anchor Bottom => new(0.5f, 1f);

    /// <summary>The canvas's bottom-right corner.</summary>
    public static Anchor BottomRight => new(1f, 1f);

    // Where this anchor lands on a canvas of the given pixel extent.
    internal Vector2 On(Vector2 canvas) => new(X * canvas.X, Y * canvas.Y);
}

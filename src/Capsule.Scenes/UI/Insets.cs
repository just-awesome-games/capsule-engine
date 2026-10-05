using System.Numerics;

namespace Capsule.UI;

/// <summary>Distances in from the four edges of a rect, in the units of the entity that holds them.</summary>
/// <remarks>A negative inset reaches outside the rect and is allowed.</remarks>
public readonly record struct Insets
{
    /// <summary>The distance in from the left edge.</summary>
    public float Left { get; }

    /// <summary>The distance down from the top edge.</summary>
    public float Top { get; }

    /// <summary>The distance in from the right edge.</summary>
    public float Right { get; }

    /// <summary>The distance up from the bottom edge.</summary>
    public float Bottom { get; }

    /// <summary>The same distance in from every edge.</summary>
    /// <param name="all">The distance in from each edge.</param>
    public Insets(float all)
        : this(all, all, all, all)
    {
    }

    /// <summary>A distance in from each edge.</summary>
    /// <param name="left">The distance in from the left edge.</param>
    /// <param name="top">The distance down from the top edge.</param>
    /// <param name="right">The distance in from the right edge.</param>
    /// <param name="bottom">The distance up from the bottom edge.</param>
    public Insets(float left, float top, float right, float bottom)
    {
        Guard.Finite(left, nameof(left));
        Guard.Finite(top, nameof(top));
        Guard.Finite(right, nameof(right));
        Guard.Finite(bottom, nameof(bottom));

        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    // The inner extent of an outer rect this wide and tall, never below zero.
    internal Vector2 Inside(Vector2 extent) =>
        Vector2.Max(extent - new Vector2(Left + Right, Top + Bottom), Vector2.Zero);
}

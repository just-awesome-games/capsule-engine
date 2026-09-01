using System.Numerics;

namespace Capsule.Collision;

/// <summary>
/// An axis-aligned box in world units, from <paramref name="Min"/> to <paramref name="Max"/> on
/// each axis. Which way each axis points is the game's convention, not this module's: an
/// <c>Aabb</c> only requires that <paramref name="Min"/> is no greater than <paramref name="Max"/>
/// component-wise.
/// </summary>
/// <param name="Min">The lower corner on both axes.</param>
/// <param name="Max">The upper corner on both axes.</param>
public readonly record struct Aabb(Vector2 Min, Vector2 Max)
{
    /// <summary>A box of <paramref name="size"/> whose lower corner is <paramref name="corner"/>.</summary>
    /// <param name="corner">The lower corner on both axes — top-left in a Y-down world.</param>
    /// <param name="size">Extent on each axis; neither component may be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is negative or not finite.</exception>
    public static Aabb FromCorner(Vector2 corner, Vector2 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X < 0f || size.Y < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A box spans a finite, non-negative extent on each axis.");
        }

        return new Aabb(corner, corner + size);
    }

    /// <summary>A box of <paramref name="size"/> centred on <paramref name="center"/>.</summary>
    /// <param name="center">The box's midpoint.</param>
    /// <param name="size">Extent on each axis; neither component may be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is negative or not finite.</exception>
    public static Aabb FromCenter(Vector2 center, Vector2 size)
    {
        Vector2 half = FromCorner(Vector2.Zero, size).Max * 0.5f;

        return new Aabb(center - half, center + half);
    }

    /// <summary>The box's midpoint.</summary>
    public Vector2 Center => (Min + Max) * 0.5f;

    /// <summary>The box's extent on each axis.</summary>
    public Vector2 Size => Max - Min;

    /// <summary>
    /// Half the box's outline length. The surface-area heuristic the dynamic tree balances by is
    /// this quantity in two dimensions.
    /// </summary>
    public float Perimeter
    {
        get
        {
            Vector2 size = Size;
            return size.X + size.Y;
        }
    }

    /// <summary>Whether the two boxes share any point, touching faces included.</summary>
    public bool Overlaps(in Aabb other) =>
        Min.X <= other.Max.X && other.Min.X <= Max.X
        && Min.Y <= other.Max.Y && other.Min.Y <= Max.Y;

    /// <summary>Whether <paramref name="other"/> lies wholly inside this box.</summary>
    public bool Contains(in Aabb other) =>
        Min.X <= other.Min.X && Min.Y <= other.Min.Y
        && other.Max.X <= Max.X && other.Max.Y <= Max.Y;

    /// <summary>Whether <paramref name="point"/> lies inside this box or on its outline.</summary>
    public bool Contains(Vector2 point) =>
        Min.X <= point.X && point.X <= Max.X && Min.Y <= point.Y && point.Y <= Max.Y;

    /// <summary>This box grown by <paramref name="margin"/> world units on every side.</summary>
    public Aabb Expanded(float margin) => new(Min - new Vector2(margin), Max + new Vector2(margin));

    /// <summary>This box moved by <paramref name="offset"/> world units.</summary>
    public Aabb Translated(Vector2 offset) => new(Min + offset, Max + offset);

    /// <summary>The smallest box holding both.</summary>
    public Aabb Union(in Aabb other) => new(Vector2.Min(Min, other.Min), Vector2.Max(Max, other.Max));

    /// <summary>The smallest box holding this one before and after a translation of <paramref name="translation"/>.</summary>
    public Aabb Swept(Vector2 translation) => Union(Translated(translation));
}

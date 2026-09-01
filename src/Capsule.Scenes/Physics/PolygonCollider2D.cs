using System.Numerics;
using Capsule.Collision;

namespace Capsule.Scenes.Physics;

/// <summary>
/// A convex polygon collider, optionally rounded. Its corners are fixed at construction and are
/// read back through <see cref="Collider2D.Shape"/>; a game that needs another outline replaces the
/// component. The corners are relative to the entity's position plus
/// <see cref="Collider2D.Offset"/>.
/// </summary>
public sealed class PolygonCollider2D : Collider2D
{
    /// <param name="points">The hull's three to eight corners, convex and in either winding order.</param>
    /// <param name="radius">How far the collider extends beyond that hull; zero for a plain polygon.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is negative or not finite.</exception>
    /// <exception cref="ArgumentException">
    /// There are not three to eight points, a point is not finite, two points nearly coincide, the
    /// points are not strictly convex, or the bounds they and the radius describe are not finite.
    /// </exception>
    public PolygonCollider2D(ReadOnlySpan<Vector2> points, float radius = 0f)
        : base(Shape2D.Polygon(points, radius))
    {
    }

    /// <summary>How far the collider extends beyond its hull, in world units; zero for a plain polygon.</summary>
    public float Radius => Shape.Radius;
}

using System.Numerics;
using Capsule.Diagnostics;
namespace Capsule.Physics;

/// <summary>
/// A convex polygon collider, optionally rounded. Its corners are fixed at construction, readable through
/// <see cref="Collider2D.Shape"/>, and relative to the entity's position plus
/// <see cref="Collider2D.Offset"/>.
/// </summary>
public sealed class PolygonCollider2D : Collider2D
{
    /// <param name="points">The hull's three to eight corners, convex and in either winding order.</param>
    /// <param name="radius">How far the collider extends beyond that hull. Zero for a plain polygon.</param>
    /// <exception cref="ArgumentException">
    /// There are not three to eight points, a point is not finite, two points nearly coincide, the
    /// points are not strictly convex, or the bounds they and the radius describe are not finite.
    /// </exception>
    public PolygonCollider2D(ReadOnlySpan<Vector2> points, float radius = 0f)
        : base(Shape2D.Polygon(points, radius))
    {
    }

    /// <summary>How far the collider extends beyond its hull, in world units. Zero for a plain polygon.</summary>
    public float Radius => Shape.Radius;

    // Draws the hull only. A rounded polygon's radius is not drawn.
    /// <inheritdoc/>
    protected internal override void OnDebugDraw()
    {
        Shape2D shape = WorldShape;
        Span<Vector2> points = stackalloc Vector2[Shape2D.MaxPoints];
        for (int index = 0; index < shape.PointCount; index++)
        {
            points[index] = shape.Point(index);
        }

        DebugDraw.Polygon(DebugDraw.Colliders, points[..shape.PointCount], DebugColor, Motion);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Points", Shape.PointCount);
        panel.Field("Radius", Radius);
    }
}

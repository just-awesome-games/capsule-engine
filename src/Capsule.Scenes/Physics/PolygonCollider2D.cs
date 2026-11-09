using System.Numerics;
using Capsule.Diagnostics;
namespace Capsule.Physics;

/// <summary>
/// A convex polygon collider, optionally rounded, whose corners are relative to the entity's position plus
/// <see cref="Collider2D.Offset"/> and readable through <see cref="Collider2D.Shape"/>.
/// </summary>
/// <remarks>
/// <see cref="SetPoints"/> replaces the corners at run time and keeps the radius. An outline that narrows
/// below twice <see cref="CollisionTolerance.LinearSlop"/> takes the collider out of the collision world
/// until one spans again, and <see cref="Collider2D.Enabled"/> keeps its value.
/// </remarks>
public sealed class PolygonCollider2D : Collider2D
{
    private const int ReshapeMaxPoints = 4;

    // How wide an outline must be to stay in the world. Twice the slop keeps every outline that is not
    // thin clear of the corners and coincident points Shape2D.Polygon refuses.
    private const float SpanningWidth = 2f * CollisionTolerance.LinearSlop;

    // Whether the last outline SetPoints received was thin. The collider is then collapsed, and Shape
    // keeps the last outline that spanned.
    private bool _thin;

    /// <param name="points">The hull's corners, convex and in either winding order.</param>
    /// <param name="radius">How far the collider extends beyond that hull. Zero for a plain polygon.</param>
    /// <exception cref="ArgumentException">The points and radius are ones <see cref="Shape2D.Polygon"/> refuses.</exception>
    public PolygonCollider2D(ReadOnlySpan<Vector2> points, float radius = 0f)
        : base(Shape2D.Polygon(points, radius))
    {
    }

    /// <summary>How far the collider extends beyond its hull, in world units. Zero for a plain polygon.</summary>
    public float Radius => Shape.Radius;

    internal override bool Steps => SurfaceVelocity != Vector2.Zero;

    /// <summary>
    /// Replaces the hull's corners with three or four <paramref name="points"/> in the collider's own units,
    /// placed and rounded as the constructor's are.
    /// </summary>
    /// <remarks>
    /// Queries and contacts see the new outline as soon as this returns. The constructor takes up to
    /// <see cref="Shape2D.MaxPoints"/> corners, and this takes at most four, which keeps an outline changed
    /// every step from allocating. An outline narrower than twice
    /// <see cref="CollisionTolerance.LinearSlop"/> across, such as a triangle whose two far corners meet,
    /// takes the collider out of the collision world as disabling does: it raises the exits it owes,
    /// <see cref="Collider2D.Enabled"/> keeps its value, and <see cref="Collider2D.Shape"/> keeps the last
    /// outline that spanned. The next outline that spans puts it back. A contact handler may call this on
    /// its own collider. A throw leaves the collider unchanged.
    /// </remarks>
    /// <param name="points">The hull's corners, convex and in either winding order.</param>
    /// <example>
    /// <code>
    /// north.SetPoints([Vector2.Zero, new Vector2(64f, -spread), new Vector2(64f, spread)]);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentException">
    /// There are fewer than three points or more than four, or an outline that spans is one
    /// <see cref="Shape2D.Polygon"/> refuses, or it cannot be placed at this offset and position.
    /// </exception>
    public void SetPoints(ReadOnlySpan<Vector2> points)
    {
        if (points.Length > ReshapeMaxPoints)
        {
            throw new ArgumentException(
                $"SetPoints takes at most {ReshapeMaxPoints} points, not {points.Length}. A collider that changes its outline keeps to {ReshapeMaxPoints} points; for a one-off outline of more, replace the collider with a new PolygonCollider2D.",
                nameof(points));
        }

        if (points.Length < 3)
        {
            throw new ArgumentException($"A polygon takes 3 or 4 points here, not {points.Length}. Give it at least three corners.", nameof(points));
        }

        for (int index = 0; index < points.Length; index++)
        {
            _ = Guard.Finite(points[index], nameof(points));
        }

        if (Width(points) < SpanningWidth)
        {
            if (!_thin)
            {
                _thin = true;
                SetShape(Shape);
            }

            return;
        }

        Shape2D shape = Shape2D.Polygon(points, Radius);
        bool wasThin = _thin;
        _thin = false;
        try
        {
            SetShape(shape);
        }
        catch
        {
            _thin = wasThin;
            throw;
        }
    }

    // A thin outline stays out of the world whatever its offset.
    private protected override bool TryPlace(in Shape2D shape, Vector2 offset, Vector2 scale, out Shape2D placed)
    {
        if (_thin)
        {
            placed = default;
            return false;
        }

        return base.TryPlace(shape, offset, scale, out placed);
    }

    // The least, over the edges, of the farthest point's distance from that edge's line. An edge no longer
    // than the slop has no line to measure from, and an outline with no such line has no width.
    private static float Width(ReadOnlySpan<Vector2> points)
    {
        float width = 0f;
        bool measured = false;
        for (int edge = 0; edge < points.Length; edge++)
        {
            Vector2 start = points[edge];
            Vector2 along = points[(edge + 1) % points.Length] - start;
            float length = along.Length();
            if (length <= CollisionTolerance.LinearSlop)
            {
                continue;
            }

            float farthest = 0f;
            for (int point = 0; point < points.Length; point++)
            {
                Vector2 toPoint = points[point] - start;
                farthest = MathF.Max(farthest, MathF.Abs((along.X * toPoint.Y) - (along.Y * toPoint.X)) / length);
            }

            width = measured ? MathF.Min(width, farthest) : farthest;
            measured = true;
        }

        return width;
    }

    // Draws the hull only. A rounded polygon's radius is not drawn.
    /// <inheritdoc/>
    protected internal override void OnDebugDraw()
    {
        Shape2D shape = WorldShape;
        Span<Vector2> points = stackalloc Vector2[shape.PointCount];
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

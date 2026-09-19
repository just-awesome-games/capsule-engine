using System.Numerics;
using Capsule.Diagnostics;
namespace Capsule.Physics;

/// <summary>
/// A stadium-shaped collider covering everything within <see cref="Radius"/> of the segment from
/// <see cref="Start"/> to <see cref="End"/>. Both endpoints are relative to the entity's position plus
/// <see cref="Collider2D.Offset"/>. Every write throws on endpoints within
/// <see cref="CollisionTolerance.LinearSlop"/> of each other, on bounds that are not finite, and on a capsule
/// that cannot be placed at the collider's offset.
/// </summary>
public sealed class CapsuleCollider2D : Collider2D
{
    private Vector2 _start;
    private Vector2 _end;
    private float _radius;

    /// <param name="start">One end of the segment, relative to the entity's position and offset.</param>
    /// <param name="end">The other end of the segment.</param>
    /// <param name="radius">How far the capsule reaches from the segment, in world units.</param>
    /// <exception cref="ArgumentException">
    /// The endpoints are within <see cref="CollisionTolerance.LinearSlop"/> of each other, or the
    /// bounds they and the radius describe are not finite.
    /// </exception>
    public CapsuleCollider2D(Vector2 start, Vector2 end, float radius)
        : base(Shape2D.Capsule(start, end, radius))
    {
        _start = start;
        _end = end;
        _radius = radius;
    }

    /// <summary>One end of the segment, relative to the entity's position and offset.</summary>
    public Vector2 Start
    {
        get => _start;
        set
        {
            SetShape(Shape2D.Capsule(value, _end, _radius));
            _start = value;
        }
    }

    /// <summary>The other end of the segment, relative to the entity's position and offset.</summary>
    public Vector2 End
    {
        get => _end;
        set
        {
            SetShape(Shape2D.Capsule(_start, value, _radius));
            _end = value;
        }
    }

    /// <summary>How far the capsule reaches from its segment, in world units.</summary>
    public float Radius
    {
        get => _radius;
        set
        {
            SetShape(Shape2D.Capsule(_start, _end, value));
            _radius = value;
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDebugDraw()
    {
        Shape2D shape = WorldShape;
        DebugDraw.Capsule(DebugDraw.Colliders, shape.Point(0), shape.Point(1), shape.Radius, DebugColor, Motion);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Start", Start);
        panel.Field("End", End);
        panel.Field("Radius", Radius);
    }
}

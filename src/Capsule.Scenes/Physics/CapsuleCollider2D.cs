using System.Numerics;
using Capsule.Collision;

namespace Capsule.Scenes.Physics;

/// <summary>
/// A stadium collider: everything within <see cref="Radius"/> of the segment from
/// <see cref="Start"/> to <see cref="End"/>. Both endpoints are relative to the entity's position
/// plus <see cref="Collider2D.Offset"/>.
/// </summary>
public sealed class CapsuleCollider2D : Collider2D
{
    private Vector2 _start;
    private Vector2 _end;
    private float _radius;

    /// <param name="start">One end of the segment, relative to the entity's position and offset.</param>
    /// <param name="end">The other end of the segment.</param>
    /// <param name="radius">How far the capsule reaches from the segment, in world units.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and greater than zero, or an endpoint is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// The endpoints are within <see cref="CollisionWorld2D.LinearSlop"/> of each other, or the
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
    /// <exception cref="ArgumentOutOfRangeException">The endpoint is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// It lies within <see cref="CollisionWorld2D.LinearSlop"/> of <see cref="End"/>, the bounds
    /// overflow, or the capsule has no place here.
    /// </exception>
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
    /// <exception cref="ArgumentOutOfRangeException">The endpoint is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// It lies within <see cref="CollisionWorld2D.LinearSlop"/> of <see cref="Start"/>, the bounds
    /// overflow, or the capsule has no place here.
    /// </exception>
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
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and greater than zero.</exception>
    /// <exception cref="ArgumentException">The bounds overflow, or the capsule has no place here.</exception>
    public float Radius
    {
        get => _radius;
        set
        {
            SetShape(Shape2D.Capsule(_start, _end, value));
            _radius = value;
        }
    }
}

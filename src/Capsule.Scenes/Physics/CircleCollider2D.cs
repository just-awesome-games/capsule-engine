using System.Numerics;
using Capsule.Collision;

namespace Capsule.Scenes.Physics;

/// <summary>
/// A circular collider, centred on the entity's position plus <see cref="Collider2D.Offset"/> —
/// a centre, not the corner a <see cref="BoxCollider2D"/> anchors by.
/// </summary>
public sealed class CircleCollider2D : Collider2D
{
    private float _radius;

    /// <param name="radius">The circle's radius, in world units.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and greater than zero.</exception>
    /// <exception cref="ArgumentException">The radius overflows the shape's bounds.</exception>
    public CircleCollider2D(float radius)
        : base(Shape2D.Circle(Vector2.Zero, radius)) => _radius = radius;

    /// <summary>How far the circle reaches from its centre, in world units.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and greater than zero.</exception>
    /// <exception cref="ArgumentException">The radius overflows the bounds, or the circle has no place here.</exception>
    public float Radius
    {
        get => _radius;
        set
        {
            SetShape(Shape2D.Circle(Vector2.Zero, value));
            _radius = value;
        }
    }
}

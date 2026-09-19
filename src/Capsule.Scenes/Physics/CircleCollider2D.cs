using System.Numerics;
using Capsule.Diagnostics;
namespace Capsule.Physics;

/// <summary>
/// A circular collider centred on the entity's position plus <see cref="Collider2D.Offset"/>. The offset
/// places its centre, unlike <see cref="BoxCollider2D"/>, which the offset anchors by its corner.
/// </summary>
public sealed class CircleCollider2D : Collider2D
{
    private float _radius;

    /// <param name="radius">The circle's radius, in world units.</param>
    /// <exception cref="ArgumentException">The radius overflows the shape's bounds.</exception>
    public CircleCollider2D(float radius)
        : base(Shape2D.Circle(Vector2.Zero, radius)) => _radius = radius;

    /// <summary>How far the circle reaches from its centre, in world units.</summary>
    public float Radius
    {
        get => _radius;
        set
        {
            SetShape(Shape2D.Circle(Vector2.Zero, value));
            _radius = value;
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDebugDraw()
    {
        Shape2D shape = WorldShape;
        DebugDraw.Circle(DebugDraw.Colliders, shape.Point(0), shape.Radius, DebugColor, Motion);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Radius", Radius);
    }
}

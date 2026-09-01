using System.Numerics;
using Capsule.Collision;

namespace Capsule.Scenes.Physics;

/// <summary>
/// An axis-aligned box collider. Anchored the way a <see cref="QuadRenderer"/> is: the box's corner
/// is the entity's position plus <see cref="Collider2D.Offset"/>, so a collider matching a drawn
/// quad is the same two values.
/// </summary>
public sealed class BoxCollider2D : Collider2D
{
    private Vector2 _size;

    /// <param name="size">The extent drawn from the corner, in world units.</param>
    /// <exception cref="ArgumentException">
    /// The size spans no more than <see cref="CollisionWorld2D.LinearSlop"/> on an axis, or the box
    /// it describes spans more than a float can measure on an axis or across both.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is negative or not finite.</exception>
    public BoxCollider2D(Vector2 size)
        : base(Shape2D.Box(Vector2.Zero, size)) => _size = size;

    /// <summary>The extent spanned from the corner, in world units.</summary>
    /// <exception cref="ArgumentException">
    /// The size spans no more than <see cref="CollisionWorld2D.LinearSlop"/> on an axis, the box it
    /// describes spans more than a float can measure, or that box has no place at this collider's
    /// offset and position.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A component of the size is negative or not finite.</exception>
    public Vector2 Size
    {
        get => _size;
        set
        {
            SetShape(Shape2D.Box(Vector2.Zero, value));
            _size = value;
        }
    }
}

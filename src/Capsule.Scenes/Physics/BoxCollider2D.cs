using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;
namespace Capsule.Physics;

/// <summary>
/// An axis-aligned box collider anchored by its corner, which sits at the entity's position plus
/// <see cref="Collider2D.Offset"/>.
/// </summary>
/// <example>
/// <code>
/// BoxCollider2D hurtbox = new(new Vector2(6f, 6f))
/// {
///     Offset = new Vector2(1f, 1f),
///     ReportsContacts = true,
/// };
/// hurtbox.SetFilter(CollisionLayers.Damaging);
/// hurtbox.ContactEntered += OnHurtboxEntered;
/// Add(hurtbox);
/// </code>
/// </example>
public sealed class BoxCollider2D : Collider2D
{
    private Vector2 _size;

    /// <param name="size">The extent drawn from the corner, in world units.</param>
    /// <exception cref="ArgumentException">
    /// The size spans no more than <see cref="CollisionTolerance.LinearSlop"/> on an axis, or the box
    /// overflows what a float can measure.
    /// </exception>
    public BoxCollider2D(Vector2 size)
        : base(Shape2D.Box(Vector2.Zero, size)) => _size = size;

    /// <summary>The extent spanned from the corner, in world units.</summary>
    /// <exception cref="ArgumentException">
    /// The size spans no more than <see cref="CollisionTolerance.LinearSlop"/> on an axis, the box overflows
    /// what a float can measure, or it cannot be placed at this offset and position.
    /// </exception>
    public Vector2 Size
    {
        get => _size;
        set
        {
            SetShape(Shape2D.Box(Vector2.Zero, value));
            _size = value;
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDebugDraw() =>
        DebugDraw.Rect(DebugDraw.Colliders, Edges(WorldShape.Bounds), DebugColor, Motion);

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Size", _size);
    }
}

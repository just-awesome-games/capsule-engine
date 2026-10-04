using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;
namespace Capsule.Physics;

/// <summary>
/// An axis-aligned box collider anchored by its corner, which sits at the entity's position plus
/// <see cref="Collider2D.Offset"/>.
/// </summary>
/// <remarks>
/// The box follows an axis-aligned scale in the entity's ancestry. Its <see cref="Collider2D.Offset"/>
/// and <see cref="Size"/> are multiplied by the entity's world scale, a negative axis mirrors the box
/// about the entity's position, and a squash stretches it. While the scaled box spans no more than
/// <see cref="CollisionTolerance.LinearSlop"/> on an axis, it leaves the collision world as though
/// disabled and <see cref="Collider2D.Enabled"/> keeps its value. A turn or a scroll factor in the
/// ancestry is refused.
/// </remarks>
/// <example>
/// <code>
/// BoxCollider2D hurtbox = new(new Vector2(6f, 6f))
/// {
///     Offset = new Vector2(1f, 1f),
///     ReportsContacts = true,
///     Detects = CollisionLayers.Damaging,
/// };
/// hurtbox.ContactEntered += OnHurtboxEntered;
/// Add(hurtbox);
/// </code>
/// </example>
public sealed class BoxCollider2D : Collider2D
{
    private Vector2 _size;

    /// <param name="size">The extent drawn from the corner, in the entity's own units.</param>
    /// <exception cref="ArgumentException">
    /// The size spans no more than <see cref="CollisionTolerance.LinearSlop"/> on an axis, or the box
    /// overflows what a float can measure.
    /// </exception>
    public BoxCollider2D(Vector2 size)
        : base(Shape2D.Box(Vector2.Zero, size)) => _size = size;

    /// <summary>The extent spanned from the corner, in the entity's own units.</summary>
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

    internal override bool Steps => false;

    internal override TransformSupport Supports => TransformSupport.Resize;

    // Scales both corners about the entity's position. A scale of one keeps the plain translation, which
    // leaves an unscaled box exactly as it was.
    private protected override bool TryPlace(in Shape2D shape, Vector2 offset, Vector2 scale, out Shape2D placed)
    {
        if (scale == Vector2.One)
        {
            placed = shape.Translated(offset);
            return true;
        }

        Aabb2D bounds = shape.Bounds;
        Vector2 first = (offset + bounds.Min) * scale;
        Vector2 second = (offset + bounds.Max) * scale;
        Vector2 min = Vector2.Min(first, second);
        Vector2 max = Vector2.Max(first, second);
        if (max.X - min.X <= CollisionTolerance.LinearSlop || max.Y - min.Y <= CollisionTolerance.LinearSlop)
        {
            placed = default;
            return false;
        }

        placed = Shape2D.Box(new Aabb2D(min, max));
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnDebugDraw()
    {
        Aabb2D box = WorldShape.Bounds;
        DebugDraw.Rect(DebugDraw.Colliders, new Rect(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y), DebugColor, Motion);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Size", _size);
    }
}

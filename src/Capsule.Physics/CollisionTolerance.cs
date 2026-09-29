namespace Capsule.Physics;

/// <summary>The world-unit tolerances every collision world works to.</summary>
public static class CollisionTolerance
{
    /// <summary>
    /// The gap a blocked move keeps from what stopped it, and the slack that keeps a flush face
    /// from reading as an overlap.
    /// </summary>
    public const float LinearSlop = 0.005f;

    /// <summary>How close two colliders must be to count as touching, in world units.</summary>
    /// <remarks>
    /// The skin is wider than <see cref="LinearSlop"/>. A body a blocked move left resting on a
    /// surface still reports contact.
    /// <para>
    /// Colliders that share an edge with no gap are within the skin and touch. A box that exactly
    /// fills the cell beside a trigger reports a contact with it. Inset the box by more than the
    /// skin to keep it clear.
    /// </para>
    /// </remarks>
    public const float ContactSkin = 0.02f;
}

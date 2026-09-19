namespace Capsule.Physics;

/// <summary>The world-unit tolerances every collision world works to.</summary>
public static class CollisionTolerance
{
    /// <summary>
    /// The gap a blocked move keeps from what stopped it, and the slack that keeps a flush face
    /// from reading as an overlap.
    /// </summary>
    public const float LinearSlop = 0.005f;

    /// <summary>
    /// How close two colliders must be to count as touching. Wider than <see cref="LinearSlop"/> so
    /// a body resting against a surface still reports contact on the next step.
    /// </summary>
    public const float ContactSkin = 0.02f;
}

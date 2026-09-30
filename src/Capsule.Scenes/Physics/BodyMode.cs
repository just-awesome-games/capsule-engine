namespace Capsule.Physics;

/// <summary>How a <see cref="KinematicBody2D"/> resolves a move against what it meets.</summary>
public enum BodyMode
{
    /// <summary>The move slides along whatever stops it, the same way on every surface.</summary>
    Floating,

    /// <summary>
    /// The move walks along floors as far as <see cref="KinematicBody2D.KeepsHorizontalSpeedOnSlopes"/>
    /// sets, rests on a slope without drifting, and follows the ground down a step or a slope.
    /// </summary>
    Grounded,
}

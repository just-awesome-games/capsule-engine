namespace Capsule.Physics;

/// <summary>How a <see cref="KinematicBody2D"/> resolves a move against what it meets.</summary>
public enum BodyMode
{
    /// <summary>The move slides along whatever stops it, the same way on every surface.</summary>
    Floating,

    /// <summary>
    /// The move walks along floors, keeps its speed on a slope, rests on one without drifting, and
    /// follows the ground down a step or a slope instead of leaving it.
    /// </summary>
    Grounded,
}

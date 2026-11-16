using System.Numerics;

namespace Capsule.Physics;

/// <summary>What one swept move actually did.</summary>
/// <param name="Translation">
/// The translation applied, the requested one cut short and slid along whatever stopped it. Add it to
/// the mover's position.
/// </param>
/// <param name="Blocked">Whether something stopped any part of the move short.</param>
/// <param name="ContactCount">
/// How many surfaces the move reached. The caller's span holds as many as fit and the rest are
/// counted only.
/// </param>
public readonly record struct MoveResult2D(Vector2 Translation, bool Blocked, int ContactCount)
{
    /// <summary>
    /// The part of <see cref="Translation"/> that a body's movement forgiveness added, or zero.
    /// </summary>
    /// <remarks>
    /// A <c>KinematicBody2D</c> sets it to the net rise of a step up, with any sink a rising step lets go of,
    /// the drop of a step down, and the shift past a ceiling's edge. A slope, a settle onto the center, a
    /// carry and a shove add nothing to it, and a world's own moves leave it zero.
    /// </remarks>
    public Vector2 Correction { get; init; }
}

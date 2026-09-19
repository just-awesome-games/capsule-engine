namespace Capsule.Input;

/// <summary>
/// An amplitude per gamepad motor, each in [0, 1]. <see cref="Rumble.Level"/> reads one for the host
/// to write to the pad, and <see cref="Rumble.Hold(RumbleLevel)"/> takes one to hold.
/// </summary>
/// <param name="Low">The low-frequency motor's amplitude.</param>
/// <param name="High">The high-frequency motor's amplitude.</param>
/// <param name="LeftTrigger">The left impulse trigger's amplitude. A pad without impulse triggers plays nothing for it.</param>
/// <param name="RightTrigger">The right impulse trigger's amplitude. A pad without impulse triggers plays nothing for it.</param>
public readonly record struct RumbleLevel(float Low, float High, float LeftTrigger, float RightTrigger)
{
    /// <summary>Every motor at rest. The default value.</summary>
    public static RumbleLevel Zero => default;

    /// <summary>Whether every motor is at rest.</summary>
    public bool IsZero => Low == 0f && High == 0f && LeftTrigger == 0f && RightTrigger == 0f;
}

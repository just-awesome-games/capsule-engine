namespace Capsule.Input;

/// <summary>
/// Everything one <see cref="Rumble.Play(in RumblePulse)"/> takes: an amplitude per motor and a
/// duration. Build it with the constructor, which sets the property defaults below.
/// </summary>
/// <remarks>
/// <c>default(RumblePulse)</c> skips them, carries no duration, and no mixer accepts it.
/// </remarks>
/// <param name="Low">The low-frequency motor's amplitude in [0, 1].</param>
/// <param name="High">The high-frequency motor's amplitude in [0, 1].</param>
/// <param name="Seconds">How long the pulse lasts, greater than zero and finite.</param>
public readonly record struct RumblePulse(float Low, float High, float Seconds)
{
    /// <summary>How the pulse changes over its duration. <see cref="RumbleFade.Decay"/> by default.</summary>
    public RumbleFade Fade { get; init; } = RumbleFade.Decay;

    /// <summary>The left impulse trigger's amplitude in [0, 1], and 0 by default.</summary>
    /// <remarks>
    /// The impulse triggers are the motors in an Xbox pad's triggers. A pad without them plays
    /// nothing for this value.
    /// </remarks>
    public float LeftTrigger { get; init; }

    /// <summary>The right impulse trigger's amplitude in [0, 1], and 0 by default.</summary>
    /// <remarks>
    /// The impulse triggers are the motors in an Xbox pad's triggers. A pad without them plays
    /// nothing for this value.
    /// </remarks>
    public float RightTrigger { get; init; }
}

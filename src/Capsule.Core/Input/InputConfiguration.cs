namespace Capsule.Input;

/// <summary>
/// Everything a game says about input: the actions its devices stand for, and the gamepad
/// deadzones its sampled pad is filtered by. A run played by an input driver — one given to
/// <c>WithInputDriver</c> or to <c>RunHeadless</c> — takes the driver's snapshots as already
/// filtered, so the deadzones apply only to a sampled gamepad.
/// </summary>
public sealed class InputConfiguration
{
    /// <summary>The stick radius a game that never sets one is filtered by.</summary>
    // Unreal's own stick deadzone, shipped in BaseInput.ini.
    public const float DefaultStickDeadzone = 0.25f;

    /// <summary>The trigger pull a game that never sets one is filtered by.</summary>
    // XInput's trigger threshold, 30 of 255.
    public const float DefaultTriggerDeadzone = 0.12f;

    /// <summary>Which buttons and axes stand for which actions.</summary>
    public ActionBindings Bindings { get; } = new();

    /// <summary>Stick radius below which the stick reads as centred.</summary>
    public float StickDeadzone { get; private set; } = DefaultStickDeadzone;

    /// <summary>Trigger pull below which the trigger reads as released.</summary>
    public float TriggerDeadzone { get; private set; } = DefaultTriggerDeadzone;

    /// <summary>
    /// A stick reading inside <paramref name="stick"/> radially reads centred and a trigger pull
    /// below <paramref name="trigger"/> reads released; past either, what remains is remapped onto
    /// [0, 1]. Defaults to 0.25 and 0.12.
    /// </summary>
    /// <param name="stick">Stick radius, in [0, 1); 0 applies no stick deadzone.</param>
    /// <param name="trigger">Trigger pull, in [0, 1); 0 applies no trigger deadzone.</param>
    /// <exception cref="ArgumentOutOfRangeException">A radius is NaN or outside [0, 1).</exception>
    public InputConfiguration GamepadDeadzones(float stick, float trigger)
    {
        RequireDeadzone(stick, nameof(stick));
        RequireDeadzone(trigger, nameof(trigger));
        StickDeadzone = stick;
        TriggerDeadzone = trigger;

        return this;
    }

    private static void RequireDeadzone(float value, string parameterName)
    {
        // NaN compares false to everything, so the range guards below cannot reject it.
        if (float.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A deadzone radius cannot be NaN.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, 1f, parameterName);
    }
}

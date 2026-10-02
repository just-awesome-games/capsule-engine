namespace Capsule.Input;

/// <summary>
/// Everything a game says about input: the actions its devices stand for, the gamepad deadzones its
/// sampled pad is filtered by, and the host-owned button that opens the debug overlay in a
/// development build.
/// </summary>
/// <remarks>
/// The deadzones apply only to a sampled gamepad. A run played by an input driver treats the
/// driver's snapshots as already filtered.
/// </remarks>
/// <example>
/// <code>
/// public static readonly AxisAction Move = new("move");
/// public static readonly InputAction Jump = new("jump");
///
/// public static void Configure(InputConfiguration input, GameSettings settings)
/// {
///     input.GamepadDeadzones(InputConfiguration.DefaultStickDeadzone, InputConfiguration.DefaultTriggerDeadzone);
///     input.Bindings.BindAxis(Move, Key.A, Key.D);
///     input.Bindings.BindAxis(Move, PadAxis.LeftStickX);
///     input.Bindings.Bind(Jump, settings.Input.Jump.Key, settings.Input.Jump.Pad);
/// }
/// </code>
/// </example>
public sealed class InputConfiguration
{
    /// <summary>The stick radius a game that never sets one is filtered by.</summary>
    public const float DefaultStickDeadzone = 0.25f;

    /// <summary>The trigger pull a game that never sets one is filtered by.</summary>
    // XInput's trigger threshold, 30 of 255.
    public const float DefaultTriggerDeadzone = 0.12f;

    /// <summary>Which buttons and axes stand for which actions.</summary>
    public ActionBindings Bindings { get; } = new();

    /// <summary>Stick radius below which the stick reads as centred, <see cref="DefaultStickDeadzone"/> until <see cref="GamepadDeadzones"/> sets it.</summary>
    public float StickDeadzone { get; private set; } = DefaultStickDeadzone;

    /// <summary>Trigger pull below which the trigger reads as released, <see cref="DefaultTriggerDeadzone"/> until <see cref="GamepadDeadzones"/> sets it.</summary>
    public float TriggerDeadzone { get; private set; } = DefaultTriggerDeadzone;

    /// <summary>
    /// The host-owned button that opens Capsule's debug overlay in a development build,
    /// <see cref="Key.Grave"/> by default. The button does not reach the simulation and wins over a
    /// game binding of the same button.
    /// </summary>
    /// <remarks>
    /// It is inert in a shipping publish, and <see cref="InputButton.None"/> disables the overlay.
    /// </remarks>
    public InputButton DebugOverlayButton { get; private set; } = Key.Grave;

    // Set when the run's first scene starts. The overlay reads DebugOverlayButton once at construction,
    // so a write after that would silently do nothing.
    internal bool Started { get; set; }

    /// <summary>
    /// A stick reading inside <paramref name="stick"/> radially reads centred, and a trigger pull
    /// below <paramref name="trigger"/> reads released. Past either threshold, the remainder is
    /// remapped onto [0, 1].
    /// </summary>
    /// <param name="stick">Stick radius, in [0, 1). Zero applies no stick deadzone.</param>
    /// <param name="trigger">Trigger pull, in [0, 1). Zero applies no trigger deadzone.</param>
    /// <exception cref="ArgumentOutOfRangeException">A radius is NaN or outside [0, 1).</exception>
    public InputConfiguration GamepadDeadzones(float stick, float trigger)
    {
        RequireDeadzone(stick, nameof(stick));
        RequireDeadzone(trigger, nameof(trigger));
        StickDeadzone = stick;
        TriggerDeadzone = trigger;

        return this;
    }

    /// <summary>
    /// Sets the host-owned button that opens Capsule's debug overlay. It may be a key, pad button,
    /// mouse button or stick direction, and <see cref="InputButton.None"/> leaves the overlay
    /// unreachable.
    /// </summary>
    /// <param name="button">The button that toggles the overlay on its leading edge.</param>
    /// <exception cref="InvalidOperationException">
    /// The run's first scene has started. Call this from <c>WithRunStart</c>.
    /// </exception>
    public InputConfiguration DebugOverlay(InputButton button)
    {
        if (Started)
        {
            throw new InvalidOperationException("The debug-overlay button is read at boot. Set it from WithRunStart.");
        }

        DebugOverlayButton = button;

        return this;
    }

    private static void RequireDeadzone(float value, string parameterName)
    {
        Guard.Finite(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, 1f, parameterName);
    }
}

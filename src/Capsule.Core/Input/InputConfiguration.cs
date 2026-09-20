namespace Capsule.Input;

/// <summary>
/// Everything a game says about input: the actions its devices stand for, the gamepad deadzones its
/// sampled pad is filtered by, and the host-owned button that opens the debug menu in a development
/// build. A run played by an input driver treats the driver's snapshots as already filtered, so the
/// deadzones apply only to a sampled gamepad.
/// </summary>
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
    /// The host-owned button that opens Capsule's debug menu in a development build. It defaults to
    /// <see cref="Key.Grave"/>, does not reach the simulation, wins over a game binding of the same
    /// button, and is inert in a shipping publish. <see cref="InputButton.None"/> disables opening the
    /// menu.
    /// </summary>
    public InputButton DebugMenuButton { get; private set; } = Key.Grave;

    // Set when the run's first scene starts. The overlay reads DebugMenuButton once at construction,
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
    /// Sets the host-owned button that opens Capsule's development debug menu. It may be a key, pad
    /// button, mouse button or stick direction, and <see cref="InputButton.None"/> leaves the menu
    /// unreachable. While the menu is open the simulation is held on the settled step and every
    /// playing voice is suspended. Closing it resumes both.
    /// </summary>
    /// <param name="button">The button that toggles the menu on its leading edge.</param>
    /// <returns>This configuration.</returns>
    /// <exception cref="InvalidOperationException">The run has already booted.</exception>
    public InputConfiguration DebugMenu(InputButton button)
    {
        if (Started)
        {
            throw new InvalidOperationException("The debug-menu button is read at boot. Set it from WithRunStart.");
        }

        DebugMenuButton = button;

        return this;
    }

    private static void RequireDeadzone(float value, string parameterName)
    {
        Guard.Finite(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, 1f, parameterName);
    }
}

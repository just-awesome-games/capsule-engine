namespace Capsule.Input;

/// <summary>
/// Everything a game says about input: which buttons and axes stand for its actions, the gamepad
/// deadzones its sampled pad is filtered by, and the host-owned button that opens the debug overlay
/// in a development build.
/// </summary>
/// <remarks>
/// A binding is read from the next step, and a rebind takes effect mid-run. The deadzones apply only
/// to a sampled gamepad. A run played by an input driver treats the driver's snapshots as already
/// filtered.
/// </remarks>
/// <example>
/// <code>
/// public static readonly AxisAction Move = new("move");
/// public static readonly InputAction Confirm = new("confirm");
///
/// public static void Configure(InputConfiguration input, GameSettings settings)
/// {
///     input.GamepadDeadzones(InputConfiguration.DefaultStickDeadzone, InputConfiguration.DefaultTriggerDeadzone);
///     input.Bind(Move, Key.A, Key.D);
///     input.Bind(Move, PadAxis.LeftStickX);
///     input.Bind(Confirm, settings.Input.Confirm.Key, settings.Input.Confirm.Pad);
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

    // The table the binding members write and every step reads. The run's input state holds this
    // instance, which is why a rebind reaches the next step.
    internal ActionBindings Bindings { get; } = new();

    // Set when the run's first scene starts. The overlay reads DebugOverlayButton once at construction,
    // and a write after that would silently do nothing.
    internal bool Started { get; set; }

    /// <summary>
    /// Adds <paramref name="buttons"/> to <paramref name="action"/>, and any of them then stands for
    /// it. Keys and pad buttons mix freely, and a second bind unions with the first.
    /// </summary>
    public InputConfiguration Bind(InputAction action, params ReadOnlySpan<InputButton> buttons)
    {
        Bindings.Bind(action, buttons);

        return this;
    }

    /// <summary>
    /// Adds the position of <paramref name="axis"/> to what <paramref name="action"/> reads. A second
    /// bind accumulates with the first, and binding the same source again changes nothing.
    /// </summary>
    public InputConfiguration Bind(AxisAction action, PadAxis axis)
    {
        Bindings.Bind(action, axis);

        return this;
    }

    /// <summary>
    /// Adds the notches <paramref name="axis"/> of the mouse wheel turns each step to what
    /// <paramref name="action"/> reads, unclamped. A second bind accumulates with the first.
    /// </summary>
    public InputConfiguration Bind(AxisAction action, MouseAxis axis)
    {
        Bindings.Bind(action, axis);

        return this;
    }

    /// <summary>
    /// Adds a digital pair to <paramref name="action"/>, reading -1 while <paramref name="negative"/>
    /// is held and +1 while <paramref name="positive"/> is. Holding both contributes 0.
    /// </summary>
    public InputConfiguration Bind(AxisAction action, InputButton negative, InputButton positive)
    {
        Bindings.Bind(action, negative, positive);

        return this;
    }

    /// <summary>
    /// Replaces every button bound to <paramref name="action"/> with <paramref name="buttons"/>,
    /// effective from the next read.
    /// </summary>
    public InputConfiguration Rebind(InputAction action, params ReadOnlySpan<InputButton> buttons)
    {
        Bindings.Rebind(action, buttons);

        return this;
    }

    /// <summary>Removes every button bound to <paramref name="action"/>, which then reads as unbound.</summary>
    public InputConfiguration Unbind(InputAction action)
    {
        Bindings.Unbind(action);

        return this;
    }

    /// <summary>Removes every source bound to <paramref name="action"/>, which then reads as unbound.</summary>
    public InputConfiguration Unbind(AxisAction action)
    {
        Bindings.Unbind(action);

        return this;
    }

    /// <summary>Buttons bound to <paramref name="action"/>. Empty when the action is unbound.</summary>
    public ReadOnlySpan<InputButton> ButtonsFor(InputAction action) => Bindings.ButtonsFor(action);

    /// <summary>
    /// A stick reading inside <paramref name="stick"/> radially reads centred, and a trigger pull
    /// below <paramref name="trigger"/> reads released. Past either threshold, the remainder is
    /// remapped onto [0, 1].
    /// </summary>
    /// <param name="stick">Stick radius, in [0, 1). Zero applies no stick deadzone.</param>
    /// <param name="trigger">Trigger pull, in [0, 1). Zero applies no trigger deadzone.</param>
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

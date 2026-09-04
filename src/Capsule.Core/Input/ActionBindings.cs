namespace Capsule.Input;

/// <summary>
/// Which buttons and axes stand for which actions. Written once at configuration time and read
/// every step thereafter; reads allocate nothing.
/// </summary>
public sealed class ActionBindings
{
    private readonly Dictionary<InputAction, InputButton[]> _buttonsByAction = [];
    private readonly Dictionary<AxisAction, AxisSource[]> _sourcesByAction = [];

    /// <summary>
    /// Adds <paramref name="buttons"/> to <paramref name="action"/>; any of them then stands for
    /// it. Keys and pad buttons mix freely, and binding twice unions rather than replaces.
    /// </summary>
    /// <exception cref="ArgumentException">The action is unnamed, or some button is <see cref="InputButton.None"/>.</exception>
    public ActionBindings Bind(InputAction action, params ReadOnlySpan<InputButton> buttons)
    {
        RequireName(action.Name, nameof(action));

        if (buttons.IsEmpty)
        {
            throw new ArgumentException("An action must be bound to at least one button.", nameof(buttons));
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            RequireButton(buttons[i], action.Name, nameof(buttons));
        }

        List<InputButton> merged = _buttonsByAction.TryGetValue(action, out InputButton[]? existing) ? [.. existing] : [];
        for (int i = 0; i < buttons.Length; i++)
        {
            if (!merged.Contains(buttons[i]))
            {
                merged.Add(buttons[i]);
            }
        }

        _buttonsByAction[action] = [.. merged];

        return this;
    }

    /// <summary>
    /// Adds <paramref name="axis"/> to <paramref name="action"/>: its position contributes to the
    /// action's value. Binding again accumulates rather than replaces.
    /// </summary>
    /// <exception cref="ArgumentException">The action is unnamed, or the axis is <see cref="PadAxis.None"/>.</exception>
    public ActionBindings BindAxis(AxisAction action, PadAxis axis)
    {
        RequireName(action.Name, nameof(action));

        if (axis == PadAxis.None)
        {
            throw new ArgumentException($"'{action.Name}' cannot be bound to {nameof(PadAxis)}.{nameof(PadAxis.None)}.", nameof(axis));
        }

        return Accumulate(action, new AxisSource(axis, InputButton.None, InputButton.None));
    }

    /// <summary>
    /// Adds a digital pair to <paramref name="action"/>: -1 while <paramref name="negative"/> is
    /// held and +1 while <paramref name="positive"/> is, so holding both contributes 0.
    /// </summary>
    /// <exception cref="ArgumentException">The action is unnamed, or either button is <see cref="InputButton.None"/>.</exception>
    public ActionBindings BindAxis(AxisAction action, InputButton negative, InputButton positive)
    {
        RequireName(action.Name, nameof(action));
        RequireButton(negative, action.Name, nameof(negative));
        RequireButton(positive, action.Name, nameof(positive));

        return Accumulate(action, new AxisSource(PadAxis.None, negative, positive));
    }

    /// <summary>Buttons bound to <paramref name="action"/>; empty when it is unbound.</summary>
    public ReadOnlySpan<InputButton> ButtonsFor(InputAction action) =>
        _buttonsByAction.TryGetValue(action, out InputButton[]? buttons) ? buttons : ReadOnlySpan<InputButton>.Empty;

    /// <summary>Whether any button bound to <paramref name="action"/> is held in <paramref name="snapshot"/>.</summary>
    public bool IsAnyDown(InputAction action, in DeviceSnapshot snapshot)
    {
        if (!_buttonsByAction.TryGetValue(action, out InputButton[]? buttons))
        {
            return false;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i].IsDown(snapshot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What <paramref name="action"/> reads in <paramref name="snapshot"/>: every bound
    /// contribution summed, then clamped to [-1, 1]; an unbound action reads 0.
    /// </summary>
    public float AxisValue(AxisAction action, in DeviceSnapshot snapshot)
    {
        if (!_sourcesByAction.TryGetValue(action, out AxisSource[]? sources))
        {
            return 0f;
        }

        float total = 0f;
        for (int i = 0; i < sources.Length; i++)
        {
            total += sources[i].Read(snapshot);
        }

        return Math.Clamp(total, -1f, 1f);
    }

    private static void RequireName(string? name, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An action must be named.", parameterName);
        }
    }

    private static void RequireButton(InputButton button, string actionName, string parameterName)
    {
        if (button.IsNone)
        {
            throw new ArgumentException(
                $"'{actionName}' cannot be bound to {nameof(InputButton)}.{nameof(InputButton.None)}.",
                parameterName);
        }
    }

    private ActionBindings Accumulate(AxisAction action, AxisSource source)
    {
        List<AxisSource> merged = _sourcesByAction.TryGetValue(action, out AxisSource[]? existing) ? [.. existing] : [];
        if (!merged.Contains(source))
        {
            merged.Add(source);
        }

        _sourcesByAction[action] = [.. merged];

        return this;
    }

    /// <summary>One contribution to an axis action: an analog axis, or else a digital pair.</summary>
    private readonly record struct AxisSource(PadAxis Analog, InputButton Negative, InputButton Positive)
    {
        internal float Read(in DeviceSnapshot snapshot) =>
            Analog != PadAxis.None
                ? snapshot.Axis(Analog)
                : (Positive.IsDown(snapshot) ? 1f : 0f) - (Negative.IsDown(snapshot) ? 1f : 0f);
    }
}

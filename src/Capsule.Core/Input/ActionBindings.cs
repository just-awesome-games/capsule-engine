namespace Capsule.Input;

/// <summary>
/// Which buttons and axes stand for which actions. Written once at configuration time and read every
/// step thereafter. A read is an array lookup and allocates nothing.
/// </summary>
public sealed class ActionBindings
{
    // Indexed by the action's own index. A read hashes nothing. A null row is an unbound action, and
    // index 0 is the unnamed action that binding refuses.
    private InputButton[]?[] _buttons = [];
    private AxisSource[]?[] _sources = [];

    /// <summary>
    /// Adds <paramref name="buttons"/> to <paramref name="action"/>, and any of them then stands for
    /// it. Keys and pad buttons mix freely, and a second bind unions with the first.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The action is unnamed, or a button is <see cref="InputButton.None"/> or outside its device's
    /// capacity.
    /// </exception>
    public ActionBindings Bind(InputAction action, params ReadOnlySpan<InputButton> buttons)
    {
        Require(action, buttons);

        ref InputButton[]? bound = ref Row(ref _buttons, action.Index);
        List<InputButton> merged = bound is { } existing ? [.. existing] : [];
        for (int i = 0; i < buttons.Length; i++)
        {
            if (!merged.Contains(buttons[i]))
            {
                merged.Add(buttons[i]);
            }
        }

        bound = [.. merged];

        return this;
    }

    /// <summary>
    /// Adds <paramref name="axis"/> to <paramref name="action"/>, so its position contributes to the
    /// action's value. A second bind accumulates with the first.
    /// </summary>
    /// <exception cref="ArgumentException">The action is unnamed, or the axis is <see cref="PadAxis.None"/>.</exception>
    public ActionBindings BindAxis(AxisAction action, PadAxis axis)
    {
        RequireName(action.Index, nameof(action));

        if (axis == PadAxis.None)
        {
            throw new ArgumentException($"'{action.Name}' cannot be bound to {nameof(PadAxis)}.{nameof(PadAxis.None)}.", nameof(axis));
        }

        return Accumulate(action, new AxisSource(axis, null, InputButton.None, InputButton.None));
    }

    /// <summary>
    /// Adds <paramref name="axis"/> of the mouse wheel to <paramref name="action"/>, so the notches
    /// turned each step contribute to the action's value without bound. A second bind accumulates
    /// with the first.
    /// </summary>
    /// <exception cref="ArgumentException">The action is unnamed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The axis names no wheel axis.</exception>
    public ActionBindings BindAxis(AxisAction action, MouseAxis axis)
    {
        RequireName(action.Index, nameof(action));

        if (axis is not (MouseAxis.ScrollX or MouseAxis.ScrollY))
        {
            throw new ArgumentOutOfRangeException(nameof(axis), axis, $"{nameof(MouseAxis)} has no such axis.");
        }

        return Accumulate(action, new AxisSource(PadAxis.None, axis, InputButton.None, InputButton.None));
    }

    /// <summary>
    /// Adds a digital pair to <paramref name="action"/>, reading -1 while <paramref name="negative"/>
    /// is held and +1 while <paramref name="positive"/> is. Holding both contributes 0.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The action is unnamed, or either button is <see cref="InputButton.None"/> or outside its
    /// device's capacity.
    /// </exception>
    public ActionBindings BindAxis(AxisAction action, InputButton negative, InputButton positive)
    {
        RequireName(action.Index, nameof(action));
        RequireButton(negative, action.Name, nameof(negative));
        RequireButton(positive, action.Name, nameof(positive));

        return Accumulate(action, new AxisSource(PadAxis.None, null, negative, positive));
    }

    /// <summary>
    /// Replaces every button bound to <paramref name="action"/> with <paramref name="buttons"/>,
    /// effective from the next read.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The action is unnamed, or a button is <see cref="InputButton.None"/> or outside its device's
    /// capacity.
    /// </exception>
    public ActionBindings Rebind(InputAction action, params ReadOnlySpan<InputButton> buttons)
    {
        Require(action, buttons);
        Row(ref _buttons, action.Index) = null;

        return Bind(action, buttons);
    }

    /// <summary>Removes every button bound to <paramref name="action"/>, which then reads as unbound.</summary>
    /// <exception cref="ArgumentException">The action is unnamed.</exception>
    public ActionBindings Unbind(InputAction action)
    {
        RequireName(action.Index, nameof(action));
        Row(ref _buttons, action.Index) = null;

        return this;
    }

    /// <summary>Removes every source bound to <paramref name="action"/>, which then reads as unbound.</summary>
    /// <exception cref="ArgumentException">The action is unnamed.</exception>
    public ActionBindings Unbind(AxisAction action)
    {
        RequireName(action.Index, nameof(action));
        Row(ref _sources, action.Index) = null;

        return this;
    }

    /// <summary>Buttons bound to <paramref name="action"/>. Empty when the action is unbound.</summary>
    public ReadOnlySpan<InputButton> ButtonsFor(InputAction action) => Bound(_buttons, action.Index);

    /// <summary>Whether any button bound to <paramref name="action"/> is held in <paramref name="snapshot"/>.</summary>
    public bool IsAnyDown(InputAction action, in DeviceSnapshot snapshot)
    {
        ReadOnlySpan<InputButton> buttons = Bound(_buttons, action.Index);

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
    /// What <paramref name="action"/> reads in <paramref name="snapshot"/>. Contributions from
    /// buttons and pad axes are summed and clamped to [-1, 1]. Wheel notches bound to the action are
    /// a count, so they add on unclamped. An unbound action reads 0.
    /// </summary>
    public float AxisValue(AxisAction action, in DeviceSnapshot snapshot)
    {
        ReadOnlySpan<AxisSource> sources = Bound(_sources, action.Index);

        float bounded = 0f;
        float notches = 0f;
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i].Wheel is null)
            {
                bounded += sources[i].Read(snapshot);
            }
            else
            {
                notches += sources[i].Read(snapshot);
            }
        }

        return Math.Clamp(bounded, -1f, 1f) + notches;
    }

    private static ReadOnlySpan<T> Bound<T>(T[]?[] rows, int index) =>
        (uint)index < (uint)rows.Length && rows[index] is { } bound ? bound : [];

    // The row for an action, growing the table to reach it. Actions are interned in declaration order.
    // A game's table is as long as the actions it declares.
    private static ref T[]? Row<T>(ref T[]?[] rows, int index)
    {
        if (index >= rows.Length)
        {
            Array.Resize(ref rows, index + 1);
        }

        return ref rows[index];
    }

    // The checks Bind and Rebind share: a named action and at least one representable button.
    private static void Require(InputAction action, ReadOnlySpan<InputButton> buttons)
    {
        RequireName(action.Index, nameof(action));

        if (buttons.IsEmpty)
        {
            throw new ArgumentException("An action must be bound to at least one button.", nameof(buttons));
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            RequireButton(buttons[i], action.Name, nameof(buttons));
        }
    }

    private static void RequireName(int index, string parameterName)
    {
        if (index == 0)
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

        // Checked once here so reading a snapshot never has to. A snapshot cannot hold a key or button
        // outside its device's capacity, so binding one would silently never fire.
        if (!button.IsRepresentable)
        {
            throw new ArgumentException(
                $"'{actionName}' cannot be bound to {button}, which is outside its device's capacity.",
                parameterName);
        }
    }

    private ActionBindings Accumulate(AxisAction action, AxisSource source)
    {
        ref AxisSource[]? bound = ref Row(ref _sources, action.Index);
        List<AxisSource> merged = bound is { } existing ? [.. existing] : [];
        if (!merged.Contains(source))
        {
            merged.Add(source);
        }

        bound = [.. merged];

        return this;
    }

    // One contribution to an axis action: a pad axis, a wheel axis, or a digital pair. A wheel source
    // reads a count of notches, not a bounded position, so AxisValue keeps it out of the clamp.
    private readonly record struct AxisSource(PadAxis Analog, MouseAxis? Wheel, InputButton Negative, InputButton Positive)
    {
        internal float Read(in DeviceSnapshot snapshot) => Wheel switch
        {
            MouseAxis.ScrollX => snapshot.Scroll.X,
            MouseAxis.ScrollY => snapshot.Scroll.Y,
            _ => Analog != PadAxis.None
                ? snapshot.Axis(Analog)
                : (Positive.IsDown(snapshot) ? 1f : 0f) - (Negative.IsDown(snapshot) ? 1f : 0f),
        };
    }
}

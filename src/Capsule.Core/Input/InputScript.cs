using System.Runtime.InteropServices;

namespace Capsule.Input;

/// <summary>
/// Builds an <see cref="InputTape"/> the way a device produces one: a held state that
/// <see cref="Down(Key)"/>, <see cref="Up(Key)"/> and <see cref="Axis"/> edit, and
/// <see cref="Wait"/> and <see cref="Tap(Key)"/> emit steps of. Every duration is a count of fixed
/// steps, never seconds.
/// </summary>
/// <remarks>
/// Editing the held state emits no step of its own, so a chord is pressed by several
/// <see cref="Down(Key)"/> calls before one <see cref="Wait"/>. A script may be built more than
/// once and carries on from where it was.
/// </remarks>
public sealed class InputScript
{
    private readonly List<DeviceSnapshot> _steps = [];

    private DeviceSnapshot _held;

    /// <summary>Holds <paramref name="key"/> down from the next emitted step on.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is not representable.</exception>
    public InputScript Down(Key key)
    {
        _held = _held.With(key);
        return this;
    }

    /// <summary>Holds <paramref name="button"/> down from the next emitted step on.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is not representable.</exception>
    public InputScript Down(PadButton button)
    {
        _held = _held.With(button);
        return this;
    }

    /// <summary>Releases <paramref name="key"/>; releasing what is not held changes nothing.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is not representable.</exception>
    public InputScript Up(Key key)
    {
        _held = _held.Without(key);
        return this;
    }

    /// <summary>Releases <paramref name="button"/>; releasing what is not held changes nothing.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is not representable.</exception>
    public InputScript Up(PadButton button)
    {
        _held = _held.Without(button);
        return this;
    }

    /// <summary>Places <paramref name="axis"/> at <paramref name="value"/> from the next emitted step on.</summary>
    /// <param name="axis">The axis to place; never <see cref="PadAxis.None"/>.</param>
    /// <param name="value">In [-1, 1] for a stick, [0, 1] for a trigger.</param>
    /// <exception cref="ArgumentOutOfRangeException">The axis names none, or the value is outside its range.</exception>
    public InputScript Axis(PadAxis axis, float value)
    {
        _held = _held.WithAxis(axis, value);
        return this;
    }

    /// <summary>Emits one step with <paramref name="key"/> held on top of the held state, then releases it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is not representable.</exception>
    /// <exception cref="InvalidOperationException">The key is already held, so the tap would release it instead.</exception>
    public InputScript Tap(Key key)
    {
        RequireNotHeld(_held.IsDown(key), $"{nameof(Key)}.{key}");

        _held = _held.With(key);
        _steps.Add(_held);
        _held = _held.Without(key);

        return this;
    }

    /// <summary>Emits one step with <paramref name="button"/> held on top of the held state, then releases it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The button is not representable.</exception>
    /// <exception cref="InvalidOperationException">The button is already held, so the tap would release it instead.</exception>
    public InputScript Tap(PadButton button)
    {
        RequireNotHeld(_held.IsDown(button), $"{nameof(PadButton)}.{button}");

        _held = _held.With(button);
        _steps.Add(_held);
        _held = _held.Without(button);

        return this;
    }

    /// <summary>Emits <paramref name="steps"/> steps of the current held state.</summary>
    /// <param name="steps">Fixed steps, never seconds; 0 emits nothing.</param>
    /// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
    public InputScript Wait(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(steps);

        for (int i = 0; i < steps; i++)
        {
            _steps.Add(_held);
        }

        return this;
    }

    /// <summary>The tape of every step emitted so far.</summary>
    public InputTape Build() => InputTape.Of(CollectionsMarshal.AsSpan(_steps));

    private static void RequireNotHeld(bool held, string name)
    {
        if (held)
        {
            throw new InvalidOperationException(
                $"{name} is already held, so a tap of it would read as a release: hold it with Down and release it with Up, or drop the Down.");
        }
    }
}

using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Input;

/// <summary>
/// Builds an <see cref="IInputDriver"/> from a fixed snapshot sequence, the way a device produces
/// one. It keeps a held state that <see cref="Down"/>, <see cref="Up"/>,
/// <see cref="Axis"/> and <see cref="MoveTo"/> edit, and <see cref="Wait"/>, <see cref="Tap"/>
/// and <see cref="Scroll"/> emit steps of that state.
/// </summary>
/// <remarks>
/// Every duration counts fixed steps, never seconds.
/// <para>
/// Editing the held state emits no step. Press a chord with several <see cref="Down"/> calls
/// followed by one <see cref="Wait"/>. The emitted sequence accumulates, and a script may be built
/// more than once. Each build covers ticks 0 to n - 1 of everything scripted so far, including
/// ticks an earlier driver already served. Write a driver that must react to the scene as a class
/// instead.
/// </para>
/// </remarks>
public sealed class InputScript
{
    private readonly List<DeviceSnapshot> _steps = [];

    private DeviceSnapshot _held;

    /// <summary>Holds <paramref name="button"/> down from the next emitted step on.</summary>
    /// <param name="button">The key, pad button, mouse button or stick direction to hold.</param>
    public InputScript Down(InputButton button)
    {
        _held = _held.With(button);
        return this;
    }

    /// <summary>Releases <paramref name="button"/>. Releasing a button that is not held changes nothing.</summary>
    /// <param name="button">The key, pad button, mouse button or stick direction to release.</param>
    public InputScript Up(InputButton button)
    {
        _held = _held.Without(button);
        return this;
    }

    /// <summary>
    /// Puts the pointer at <paramref name="position"/> from the next emitted step on, the way a
    /// mouse moved there would. The position is in canvas pixels from the canvas's top-left corner.
    /// </summary>
    /// <remarks>It is not clamped, and the pointer can sit outside the canvas.</remarks>
    public InputScript MoveTo(Vector2 position)
    {
        _held = _held.WithPointer(position);
        return this;
    }

    /// <summary>Gives the game's window focus or takes it away from the next emitted step on. A script starts focused.</summary>
    public InputScript WindowFocus(bool focused)
    {
        _held = _held.WithWindowFocus(focused);
        return this;
    }

    /// <summary>Places <paramref name="axis"/> at <paramref name="value"/> from the next emitted step on.</summary>
    /// <param name="axis">The axis to place. Not <see cref="PadAxis.None"/>.</param>
    /// <param name="value">In [-1, 1] for a stick, or [0, 1] for a trigger.</param>
    public InputScript Axis(PadAxis axis, float value)
    {
        _held = _held.WithAxis(axis, value);
        return this;
    }

    /// <summary>Emits one step with <paramref name="button"/> held on top of the held state, then releases it.</summary>
    /// <param name="button">The key, pad button, mouse button or stick direction to tap.</param>
    /// <exception cref="InvalidOperationException">The button is already held. Release it with <see cref="Up"/> first.</exception>
    public InputScript Tap(InputButton button)
    {
        if (button.IsDown(_held))
        {
            throw new InvalidOperationException(
                $"{button} is already held, and a tap would read as a release. Release it with Up first, or drop the Down.");
        }

        _steps.Add(_held.With(button));

        return this;
    }

    /// <summary>
    /// Emits one step with the wheel turned <paramref name="notches"/> on top of the held state, then
    /// stills the wheel. Only that one step sees the notches.
    /// </summary>
    /// <param name="notches">Wheel notches. Positive X scrolls right and positive Y scrolls away from the user. Unbounded.</param>
    public InputScript Scroll(Vector2 notches)
    {
        _steps.Add(_held.WithScroll(notches));

        return this;
    }

    /// <summary>Emits <paramref name="steps"/> steps of the current held state.</summary>
    /// <param name="steps">A count of fixed steps, never seconds. Zero emits nothing.</param>
    public InputScript Wait(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(steps);

        for (int i = 0; i < steps; i++)
        {
            _steps.Add(_held);
        }

        return this;
    }

    /// <summary>
    /// Builds a driver over every step emitted so far, indexed by the run's own tick. The step at
    /// tick <c>t</c> is position <c>t</c> of the sequence.
    /// </summary>
    /// <remarks>
    /// <c>n</c> steps drive ticks 0 to <c>n</c> - 1, and every tick at or past <c>n</c> is declined
    /// and ends the run.
    /// <para>
    /// Positions are absolute, not relative to where the driver was built. Given a run already at
    /// tick 30, the script serves its position 30, which exists only when it emitted more than 30
    /// steps. A shorter script ends the run without a step, and a script that emitted nothing ends
    /// any run before its first step.
    /// </para>
    /// </remarks>
    public IInputDriver Build() => new ScriptedInputDriver([.. _steps]);
}

// The driver InputScript builds: a fixed sequence that never reads the scene.
internal sealed class ScriptedInputDriver(DeviceSnapshot[] steps) : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        if (tick < 0 || tick >= steps.Length)
        {
            snapshot = DeviceSnapshot.Empty;

            return false;
        }

        snapshot = steps[(int)tick];

        return true;
    }
}

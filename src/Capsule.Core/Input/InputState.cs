using System.Numerics;

namespace Capsule.Input;

/// <summary>Action-level input derived from consecutive deterministic device snapshots.</summary>
public sealed class InputState(ActionBindings bindings)
{
    private readonly ActionBindings _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

    private DeviceSnapshot _previous;
    private DeviceSnapshot _current;

    /// <summary>
    /// Where the pointer sits this step, in canvas pixels from the canvas's top-left corner.
    /// Unclamped. A pointer off the canvas reads outside it.
    /// </summary>
    public Vector2 Pointer => _current.Pointer;

    /// <summary>Whether the pointer sits somewhere other than where it was the previous step.</summary>
    public bool PointerMoved => _current.Pointer != _previous.Pointer;

    /// <summary>How far the pointer moved into this step, in canvas pixels. Zero while it rests.</summary>
    public Vector2 PointerDelta => _current.Pointer - _previous.Pointer;

    /// <summary>
    /// Wheel notches turned this step, and zero while the wheel rests. X positive scrolls right, Y
    /// positive scrolls away from the user. Unbounded. A flick reads several notches at once.
    /// </summary>
    public Vector2 Scroll => _current.Scroll;

    /// <summary>Advances to a snapshot. Repeating a snapshot produces no second edge.</summary>
    public void Advance(in DeviceSnapshot snapshot)
    {
        _previous = _current;
        _current = snapshot;
    }

    /// <summary>Whether anything bound to <paramref name="action"/> is down this step.</summary>
    public bool IsHeld(InputAction action) => _bindings.IsAnyDown(action, _current);

    /// <summary>
    /// What <paramref name="action"/> reads this step. Buttons and pad axes contribute within [-1, 1],
    /// and wheel notches bound to it add on unbounded. An unbound action reads 0.
    /// </summary>
    public float Axis(AxisAction action) => _bindings.AxisValue(action, _current);

    /// <summary>Whether <paramref name="action"/> went down on the edge into this step.</summary>
    public bool WasPressed(InputAction action) =>
        _bindings.IsAnyDown(action, _current) && !_bindings.IsAnyDown(action, _previous);

    /// <summary>Whether <paramref name="action"/> came up on the edge into this step.</summary>
    public bool WasReleased(InputAction action) =>
        !_bindings.IsAnyDown(action, _current) && _bindings.IsAnyDown(action, _previous);
}

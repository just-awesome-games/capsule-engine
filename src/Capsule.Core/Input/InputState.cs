using System.Numerics;

namespace Capsule.Input;

/// <summary>Action-level input derived from consecutive deterministic device snapshots.</summary>
public sealed class InputState(ActionBindings bindings)
{
    private readonly ActionBindings _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

    // How far the pointer moves in one step before the mouse counts as used. Mouse sensors report a
    // pixel of drift while a hand rests on a pad.
    private const float PointerActivationPixels = 2f;

    private DeviceSnapshot _previous;
    private DeviceSnapshot _current;
    private InputDevice _activeDevice;
    private bool _activeDeviceChanged;

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

    /// <summary>The device the player last used, which a button prompt reads.</summary>
    /// <remarks>
    /// It becomes <see cref="InputDevice.Gamepad"/> on a step a pad button goes down or a pad axis is
    /// off centre, and <see cref="InputDevice.KeyboardMouse"/> on a step a key or mouse button goes
    /// down, the wheel turns or the pointer moves more than two canvas pixels. A button that stays
    /// held changes nothing. The pad wins a step both devices act on. The seed is part of the run's
    /// initial state, from a pad found at boot. Every later value is derived from the snapshots
    /// alone, and a replay reproduces it.
    /// </remarks>
    public InputDevice ActiveDevice => _activeDevice;

    /// <summary>Whether <see cref="ActiveDevice"/> is not what it was the previous step.</summary>
    public bool ActiveDeviceChanged => _activeDeviceChanged;

    /// <summary>Advances to a snapshot. Repeating a snapshot produces no second edge.</summary>
    public void Advance(in DeviceSnapshot snapshot)
    {
        _previous = _current;
        _current = snapshot;

        InputDevice device = DeviceUsed(in _current, in _previous) ?? _activeDevice;
        _activeDeviceChanged = device != _activeDevice;
        _activeDevice = device;
    }

    // The initial device, part of the run's initial state. Set before the first step and raises no
    // change.
    internal void Seed(InputDevice device)
    {
        _activeDevice = device;
        _activeDeviceChanged = false;
    }

    // The device that acted on the edge into this step, or null when neither did.
    private static InputDevice? DeviceUsed(in DeviceSnapshot current, in DeviceSnapshot previous)
    {
        if (current.AnyPadButtonNewlyDown(in previous) || current.AnyPadAxisActive)
        {
            return InputDevice.Gamepad;
        }

        if (current.AnyKeyOrMouseButtonNewlyDown(in previous)
            || current.Scroll != Vector2.Zero
            || Vector2.Distance(current.Pointer, previous.Pointer) > PointerActivationPixels)
        {
            return InputDevice.KeyboardMouse;
        }

        return null;
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

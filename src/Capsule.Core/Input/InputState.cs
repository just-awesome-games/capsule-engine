using System.Numerics;

namespace Capsule.Input;

/// <summary>
/// Action-level input derived from consecutive device snapshots. The engine owns the run's instance,
/// reached as <see cref="StepContext.Input"/>, and advances it once per fixed step before the step
/// runs.
/// </summary>
public sealed class InputState
{
    private readonly ActionBindings _bindings;

    // How far the pointer moves in one step before the mouse counts as used. Mouse sensors report a
    // pixel of drift while a hand rests on a pad.
    private const float PointerActivationPixels = 2f;

    private DeviceSnapshot _previous;
    private DeviceSnapshot _current;
    private InputDevice _activeDevice;
    private bool _activeDeviceChanged;

    internal InputState(ActionBindings bindings) =>
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

    /// <summary>
    /// Where the pointer sits this step, in canvas pixels from the canvas's top-left corner.
    /// Unclamped.
    /// </summary>
    /// <remarks>A pointer off the canvas reads outside it.</remarks>
    public Vector2 Pointer => _current.Pointer;

    /// <summary>Whether the pointer sits somewhere other than where it was the previous step.</summary>
    public bool PointerMoved => _current.Pointer != _previous.Pointer;

    /// <summary>How far the pointer moved into this step, in canvas pixels. Zero while it rests.</summary>
    public Vector2 PointerDelta => _current.Pointer - _previous.Pointer;

    /// <summary>
    /// Wheel notches turned this step, and zero while the wheel rests. X positive scrolls right, Y
    /// positive scrolls away from the user.
    /// </summary>
    /// <remarks>Unbounded. A flick reads several notches at once.</remarks>
    public Vector2 Scroll => _current.Scroll;

    /// <summary>The device the player last used, which a button prompt reads.</summary>
    /// <remarks>
    /// It becomes <see cref="InputDevice.Gamepad"/> on a step a pad button goes down or a pad axis is
    /// off centre, and <see cref="InputDevice.KeyboardMouse"/> on a step a key or mouse button goes
    /// down, the wheel turns or the pointer moves more than two canvas pixels. A button that stays
    /// held changes nothing. The pad wins a step both devices act on. A run starts on
    /// <see cref="InputDevice.Gamepad"/> when it finds a pad at boot and no input driver plays it.
    /// Every later value is derived from the snapshots alone, and a replay reproduces it.
    /// </remarks>
    public InputDevice ActiveDevice => _activeDevice;

    /// <summary>Whether <see cref="ActiveDevice"/> is not what it was the previous step.</summary>
    public bool ActiveDeviceChanged => _activeDeviceChanged;

    // Advances to a snapshot. Repeating a snapshot produces no second edge.
    internal void Advance(in DeviceSnapshot snapshot)
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
    /// What <paramref name="action"/> reads this step. Buttons and pad axes contribute within [-1,
    /// 1], and wheel notches bound to it add on unbounded.
    /// </summary>
    /// <remarks>An unbound action reads 0.</remarks>
    public float Axis(AxisAction action) => _bindings.AxisValue(action, _current);

    /// <summary>Whether <paramref name="action"/> went down on the edge into this step.</summary>
    public bool WasPressed(InputAction action) =>
        _bindings.IsAnyDown(action, _current) && !_bindings.IsAnyDown(action, _previous);

    /// <summary>Whether <paramref name="action"/> came up on the edge into this step.</summary>
    public bool WasReleased(InputAction action) =>
        !_bindings.IsAnyDown(action, _current) && _bindings.IsAnyDown(action, _previous);

    /// <summary>
    /// Whether any button went down on the edge into this step, and which. When several did, a key
    /// wins over a mouse button, then a pad button, then a stick direction, lowest value first.
    /// </summary>
    /// <param name="button">The button that went down, or <see cref="InputButton.None"/> when none did.</param>
    public bool WasAnyPressed(out InputButton button)
    {
        if (_current.NewlyDownKey(in _previous) is { } key)
        {
            button = key;
            return true;
        }

        if (_current.NewlyDownMouseButton(in _previous) is { } mouseButton)
        {
            button = mouseButton;
            return true;
        }

        if (_current.NewlyDownPadButton(in _previous) is { } padButton)
        {
            button = padButton;
            return true;
        }

        for (StickDirection direction = StickDirection.LeftStickUp; direction <= StickDirection.RightStickRight; direction++)
        {
            InputButton candidate = direction;
            if (candidate.IsDown(_current) && !candidate.IsDown(_previous))
            {
                button = candidate;
                return true;
            }
        }

        button = InputButton.None;
        return false;
    }
}

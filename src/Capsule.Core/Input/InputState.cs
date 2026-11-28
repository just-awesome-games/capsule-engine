using System.Numerics;

namespace Capsule.Input;

/// <summary>
/// Action-level input derived from consecutive device snapshots. The engine owns the run's instance,
/// reached as <see cref="StepContext.Input"/>, and advances it once per fixed step before the step
/// runs.
/// </summary>
public sealed class InputState
{
    // How far the pointer moves in one step before the mouse counts as used. Mouse sensors report a
    // pixel of drift while a hand rests on a pad.
    private const float PointerActivationPixels = 2f;

    private readonly ActionBindings _bindings;
    private DeviceSnapshot _previous;
    private DeviceSnapshot _current;
    private InputDevice _activeDevice;
    private bool _activeDeviceChanged;
    private bool _padLost;

    // The characters the player's keyboard layout prints, indexed by key, or null for a US layout's.
    // A null entry is a key the layout prints no character on. The array is never rewritten, a change
    // installs a new one.
    private string?[]? _keyLayout;

    // Counts every layout installed. A label naming a button follows it.
    internal int KeyLayoutVersion { get; private set; }

    internal InputState(ActionBindings bindings) => _bindings = bindings;

    /// <summary>
    /// Where the pointer sits this step, in canvas pixels from the canvas's top-left corner.
    /// Unclamped.
    /// </summary>
    /// <remarks>
    /// A still mouse reads a new position on a step the canvas moved under it, as when the window is
    /// resized or the screen layer is placed anew.
    /// </remarks>
    public Vector2 Pointer => _current.Pointer;

    /// <summary>Whether the player moved the pointer into this step.</summary>
    /// <remarks>
    /// False on a step the canvas moved under the pointer, as when the window is resized or the screen
    /// layer is placed anew, even where the hand moved the mouse too. The OS can report a still mouse's
    /// place in a changed window a sample late, and the step that sample reaches reads no motion either.
    /// </remarks>
    public bool PointerMoved => !_current.PointerRemapped && _current.Pointer != _previous.Pointer;

    /// <summary>How far the player moved the pointer into this step, in canvas pixels. Zero while it rests.</summary>
    /// <remarks>
    /// Zero on each step <see cref="PointerMoved"/> reads false for a moved canvas.
    /// </remarks>
    public Vector2 PointerDelta => _current.PointerRemapped ? Vector2.Zero : _current.Pointer - _previous.Pointer;

    /// <summary>
    /// Wheel notches turned this step, and zero while the wheel rests. X positive scrolls right, Y
    /// positive scrolls away from the user.
    /// </summary>
    /// <remarks>Unbounded.</remarks>
    public Vector2 Scroll => _current.Scroll;

    /// <summary>Whether the game's window has input focus this step.</summary>
    /// <remarks>
    /// The engine does not pause on a loss. An unfocused window reads no key, button or stick from any
    /// device. A driven or headless run has window focus unless its driver takes it away.
    /// </remarks>
    public bool HasWindowFocus => _current.HasWindowFocus;

    /// <summary>Whether the window lost focus on the edge into this step.</summary>
    public bool WindowFocusLost => !_current.HasWindowFocus && _previous.HasWindowFocus;

    /// <summary>Whether the window regained focus on the edge into this step.</summary>
    public bool WindowFocusGained => _current.HasWindowFocus && !_previous.HasWindowFocus;

    /// <summary>Whether a pad is in use this step, which a prompt to connect one reads.</summary>
    /// <remarks>
    /// False for a player with no pad connected. A game holds play on <see cref="PadLost"/> and not on this.
    /// The pad in use is the earliest connected at boot, then the one last pressed. It goes when that pad
    /// disconnects, and the next press on another pad, or a pad connecting while none is in use, brings one
    /// back. A driven or headless run has a pad unless its driver takes it away.
    /// </remarks>
    public bool HasPad => _current.HasPad;

    /// <summary>
    /// Whether the pad the player was playing with disconnected on the edge into this step, where a game holds play.
    /// </summary>
    /// <remarks>
    /// Set only while <see cref="ActiveDevice"/> was <see cref="InputDevice.Gamepad"/> before this step. A
    /// loss after a step where the mouse moved the device to <see cref="InputDevice.KeyboardMouse"/> is not
    /// reported either. The loss itself leaves <see cref="ActiveDevice"/> where this step's input puts it.
    /// </remarks>
    public bool PadLost => _padLost;

    /// <summary>The device the player last used, which a button prompt reads.</summary>
    /// <remarks>
    /// It becomes <see cref="InputDevice.Gamepad"/> on a step a pad button goes down or a pad axis is
    /// off centre, and <see cref="InputDevice.KeyboardMouse"/> on a step a key or mouse button goes
    /// down, the wheel turns or the player moves the pointer more than two canvas pixels. A button that
    /// stays held changes nothing, and the canvas moving under a still mouse is no use of it. The pad wins
    /// a step both devices act on. A run starts on <see cref="InputDevice.Gamepad"/> when it finds a pad at
    /// boot and no input driver plays it. Every later value is derived from the snapshots alone, and a
    /// replay reproduces it.
    /// </remarks>
    public InputDevice ActiveDevice => _activeDevice;

    /// <summary>Whether <see cref="ActiveDevice"/> is not what it was the previous step.</summary>
    public bool ActiveDeviceChanged => _activeDeviceChanged;

    /// <summary>
    /// The family of the pad the player last used this step, which picks a pad button's name and a prompt's art.
    /// <see cref="Input.PadFamily.Generic"/> until a pad of a known family is found.
    /// </summary>
    /// <remarks>
    /// It keeps the last pad's family after the pad is disconnected, and a replay reproduces it. A
    /// change of family alone does not change <see cref="ActiveDevice"/>.
    /// </remarks>
    public PadFamily PadFamily => _current.PadFamily;

    /// <summary>
    /// Whether the player's pad has <paramref name="button"/> this step, as its mapping defines it.
    /// Every button reads present until a pad is found, and <see cref="PadButton.None"/> never does.
    /// </summary>
    /// <remarks>
    /// The answer comes from the pad's mapping and not its hardware, and a button the mapping lacks can
    /// never be pressed. It keeps the last pad's buttons after the pad is disconnected, and a replay
    /// reproduces it.
    /// </remarks>
    public bool PadHas(PadButton button) => _current.PadHas(button);

    /// <summary>Whether the buttons <see cref="PadHas"/> reads are not those of the previous step.</summary>
    public bool PadButtonsChanged => _current.PadLackingBits != _previous.PadLackingBits;

    /// <summary>
    /// The character the player's keyboard layout prints on <paramref name="button"/>'s key position,
    /// uppercased, as <c>Z</c> for <see cref="Key.W"/> on an AZERTY keyboard.
    /// </summary>
    /// <remarks>
    /// Null for a key that prints no character, such as <see cref="Key.LeftShift"/>, <see cref="Key.F1"/>,
    /// an arrow, <see cref="Key.Enter"/> or <see cref="Key.Space"/>, for every key of the numeric keypad,
    /// which prints the same on every layout, and for a button that is not a key. A run an input driver
    /// plays and a headless run read a US layout's characters. A repeated read allocates nothing.
    /// </remarks>
    public string? KeyCharacter(InputButton button)
    {
        string?[] characters = _keyLayout ?? KeyCharacters.Us;

        return button.AsKey is { } key && (uint)key < (uint)characters.Length ? characters[(int)key] : null;
    }

    /// <summary>
    /// The button a prompt for <paramref name="action"/> shows this step: the first one bound on
    /// <see cref="ActiveDevice"/>, else the first one bound on any device.
    /// </summary>
    /// <remarks>
    /// <see cref="InputButton.None"/> where the action has no button. A label given the action shows the
    /// same button and follows it by itself.
    /// </remarks>
    public InputButton ButtonFor(InputAction action)
    {
        ReadOnlySpan<InputButton> bound = _bindings.ButtonsFor(action);
        foreach (InputButton button in bound)
        {
            if (button.Device == _activeDevice)
            {
                return button;
            }
        }

        return bound.IsEmpty ? InputButton.None : bound[0];
    }

    // Advances to a snapshot. Repeating a snapshot produces no second edge.
    internal void Advance(in DeviceSnapshot snapshot)
    {
        _previous = _current;
        _current = snapshot;

        InputDevice device = DeviceUsed(in _current, in _previous) ?? _activeDevice;
        _padLost = _activeDevice == InputDevice.Gamepad && _previous.HasPad && !_current.HasPad;
        _activeDeviceChanged = device != _activeDevice;
        _activeDevice = device;
    }

    // Reads key characters from the array, which the caller no longer changes, from now on, or from a US
    // layout's when null. The scheduler installs one at the top of a step, and a driven or headless run
    // keeps the US characters it replays with.
    internal void UseKeyLayout(string?[]? characters)
    {
        _keyLayout = characters;
        KeyLayoutVersion++;
    }

    // The layout key characters are read from, null for a US layout's.
    internal string?[]? KeyLayout => _keyLayout;

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
            || (!current.PointerRemapped && Vector2.Distance(current.Pointer, previous.Pointer) > PointerActivationPixels))
        {
            return InputDevice.KeyboardMouse;
        }

        return null;
    }

    /// <summary>Whether anything bound to <paramref name="action"/> is down this step.</summary>
    public bool IsHeld(InputAction action) => Held(action, null);

    /// <summary>
    /// What <paramref name="action"/> reads this step. Buttons and pad axes contribute within [-1,
    /// 1], and wheel notches bound to it add on unbounded.
    /// </summary>
    /// <remarks>An unbound action reads 0.</remarks>
    public float Axis(AxisAction action) => _bindings.AxisValue(action, _current);

    /// <summary>Whether <paramref name="action"/> went down on the edge into this step.</summary>
    public bool WasPressed(InputAction action) => Pressed(action, null);

    internal bool WasPressed(InputAction action, bool mouse) => Pressed(action, mouse);

    internal bool IsHeld(InputAction action, bool mouse) => Held(action, mouse);

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

    // Whether action is held, through only its mouse buttons, only the rest, or all of them when mouse is null.
    private bool Held(InputAction action, bool? mouse) => _bindings.IsAnyDown(action, _current, mouse);

    private bool Pressed(InputAction action, bool? mouse) =>
        Held(action, mouse) && !_bindings.IsAnyDown(action, _previous, mouse);
}

namespace Capsule.Input;

/// <summary>The device the player last used, as <see cref="InputState.ActiveDevice"/> reads it.</summary>
public enum InputDevice : byte
{
    /// <summary>The keyboard or the mouse. The seed of a run with no pad.</summary>
    KeyboardMouse,

    /// <summary>The gamepad.</summary>
    Gamepad,
}

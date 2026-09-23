namespace Capsule.Input;

/// <summary>The device the player last used, as <see cref="InputState.ActiveDevice"/> reads it.</summary>
public enum InputDevice : byte
{
    /// <summary>The keyboard or the mouse, and the starting device of a run that finds no pad at boot.</summary>
    KeyboardMouse,

    /// <summary>The gamepad.</summary>
    Gamepad,
}

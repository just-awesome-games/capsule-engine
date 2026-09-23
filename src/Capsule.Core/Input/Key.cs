namespace Capsule.Input;

/// <summary>A physical key, named for what a US layout prints on it.</summary>
/// <remarks>
/// A binding follows the position, not the character. <see cref="None"/> is the default and names
/// no key. Values must stay below <see cref="DeviceSnapshot.Capacity"/>.
/// </remarks>
#pragma warning disable CS1591 // Each member's name is the key it stands for.
public enum Key
{
    None,

    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,

    // The number row, never the keypad.
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,

    // The arrow keys.
    Left,
    Right,
    Up,
    Down,

    Escape,

    // The main Enter or Return key.
    Enter,
    Space,
    Tab,
    Backspace,

    // Each modifier names the side of the keyboard it sits on. RightAlt is AltGr on layouts with one.
    LeftShift,
    RightShift,
    LeftControl,
    RightControl,
    LeftAlt,
    RightAlt,

    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,

    // The grave accent and tilde key left of the 1 key.
    Grave,
}
#pragma warning restore CS1591

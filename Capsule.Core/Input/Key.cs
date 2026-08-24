namespace Capsule.Input;

/// <summary>
/// A physical key. Values must stay below <see cref="DeviceSnapshot.Capacity"/>;
/// the backend maps each one.
/// </summary>
public enum Key
{
    /// <summary>No key. The default, so an unassigned <see cref="Key"/> never means a real key.</summary>
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

    Left,
    Right,
    Up,
    Down,

    Escape,
    Enter,
    Space,
    Tab,
    Backspace,

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
}

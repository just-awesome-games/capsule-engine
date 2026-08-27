namespace Capsule.Input;

/// <summary>
/// A physical key, named for what a US layout prints on it: what the player's own layout prints
/// there may differ, and a binding follows the position rather than the character. Values must
/// stay below <see cref="DeviceSnapshot.Capacity"/>; the backend maps each one.
/// </summary>
public enum Key
{
    /// <summary>No key. The default, so an unassigned <see cref="Key"/> never means a real key.</summary>
    None,

#pragma warning disable CS1591 // A letter key's name is what it prints; a comment here could only restate it.
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
#pragma warning restore CS1591

    /// <summary>The <c>0</c> key on the number row, never the keypad.</summary>
    Digit0,

    /// <summary>The <c>1</c> key on the number row, never the keypad.</summary>
    Digit1,

    /// <summary>The <c>2</c> key on the number row, never the keypad.</summary>
    Digit2,

    /// <summary>The <c>3</c> key on the number row, never the keypad.</summary>
    Digit3,

    /// <summary>The <c>4</c> key on the number row, never the keypad.</summary>
    Digit4,

    /// <summary>The <c>5</c> key on the number row, never the keypad.</summary>
    Digit5,

    /// <summary>The <c>6</c> key on the number row, never the keypad.</summary>
    Digit6,

    /// <summary>The <c>7</c> key on the number row, never the keypad.</summary>
    Digit7,

    /// <summary>The <c>8</c> key on the number row, never the keypad.</summary>
    Digit8,

    /// <summary>The <c>9</c> key on the number row, never the keypad.</summary>
    Digit9,

    /// <summary>The left arrow key.</summary>
    Left,

    /// <summary>The right arrow key.</summary>
    Right,

    /// <summary>The up arrow key.</summary>
    Up,

    /// <summary>The down arrow key.</summary>
    Down,

    /// <summary>The <c>Esc</c> key.</summary>
    Escape,

    /// <summary>The main <c>Enter</c> or <c>Return</c> key.</summary>
    Enter,

    /// <summary>The space bar.</summary>
    Space,

    /// <summary>The <c>Tab</c> key.</summary>
    Tab,

    /// <summary>The <c>Backspace</c> key.</summary>
    Backspace,

    /// <summary>The left <c>Shift</c> key, distinct from the right one.</summary>
    LeftShift,

    /// <summary>The right <c>Shift</c> key, distinct from the left one.</summary>
    RightShift,

    /// <summary>The left <c>Ctrl</c> key, distinct from the right one.</summary>
    LeftControl,

    /// <summary>The right <c>Ctrl</c> key, distinct from the left one.</summary>
    RightControl,

    /// <summary>The left <c>Alt</c> key, distinct from the right one.</summary>
    LeftAlt,

    /// <summary>The right <c>Alt</c> key — <c>AltGr</c> on layouts that have one.</summary>
    RightAlt,

#pragma warning disable CS1591 // A function key's name is what it prints; a comment here could only restate it.
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
#pragma warning restore CS1591
}

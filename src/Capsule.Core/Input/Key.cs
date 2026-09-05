namespace Capsule.Input;

/// <summary>
/// A physical key, named for what a US layout prints on it: a binding follows the position rather
/// than the character. Values must stay below <see cref="DeviceSnapshot.Capacity"/>.
/// </summary>
public enum Key
{
    /// <summary>No key. The default, so an unassigned <see cref="Key"/> never means a real key.</summary>
    None,

    /// <summary>The <c>A</c> key.</summary>
    A,

    /// <summary>The <c>B</c> key.</summary>
    B,

    /// <summary>The <c>C</c> key.</summary>
    C,

    /// <summary>The <c>D</c> key.</summary>
    D,

    /// <summary>The <c>E</c> key.</summary>
    E,

    /// <summary>The <c>F</c> key.</summary>
    F,

    /// <summary>The <c>G</c> key.</summary>
    G,

    /// <summary>The <c>H</c> key.</summary>
    H,

    /// <summary>The <c>I</c> key.</summary>
    I,

    /// <summary>The <c>J</c> key.</summary>
    J,

    /// <summary>The <c>K</c> key.</summary>
    K,

    /// <summary>The <c>L</c> key.</summary>
    L,

    /// <summary>The <c>M</c> key.</summary>
    M,

    /// <summary>The <c>N</c> key.</summary>
    N,

    /// <summary>The <c>O</c> key.</summary>
    O,

    /// <summary>The <c>P</c> key.</summary>
    P,

    /// <summary>The <c>Q</c> key.</summary>
    Q,

    /// <summary>The <c>R</c> key.</summary>
    R,

    /// <summary>The <c>S</c> key.</summary>
    S,

    /// <summary>The <c>T</c> key.</summary>
    T,

    /// <summary>The <c>U</c> key.</summary>
    U,

    /// <summary>The <c>V</c> key.</summary>
    V,

    /// <summary>The <c>W</c> key.</summary>
    W,

    /// <summary>The <c>X</c> key.</summary>
    X,

    /// <summary>The <c>Y</c> key.</summary>
    Y,

    /// <summary>The <c>Z</c> key.</summary>
    Z,

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

    /// <summary>The <c>F1</c> function key.</summary>
    F1,

    /// <summary>The <c>F2</c> function key.</summary>
    F2,

    /// <summary>The <c>F3</c> function key.</summary>
    F3,

    /// <summary>The <c>F4</c> function key.</summary>
    F4,

    /// <summary>The <c>F5</c> function key.</summary>
    F5,

    /// <summary>The <c>F6</c> function key.</summary>
    F6,

    /// <summary>The <c>F7</c> function key.</summary>
    F7,

    /// <summary>The <c>F8</c> function key.</summary>
    F8,

    /// <summary>The <c>F9</c> function key.</summary>
    F9,

    /// <summary>The <c>F10</c> function key.</summary>
    F10,

    /// <summary>The <c>F11</c> function key.</summary>
    F11,

    /// <summary>The <c>F12</c> function key.</summary>
    F12,
}

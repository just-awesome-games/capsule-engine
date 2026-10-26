using System.Runtime.InteropServices;
using Capsule.Input;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Input;

// Turns the OS keyboard into a DeviceSnapshot. The only place hardware enters the engine. A key is
// read by its scancode, the key's position, whatever character the active layout prints on it, and
// that character is read separately, for InputState.KeyCharacter.
internal static class KeyboardSampler
{
    private static readonly Scancode[] ScancodeByKey = BuildLookup();

    // The length of a key layout table, which is indexed by key.
    internal static int KeyCount => ScancodeByKey.Length;

    // The keys held now, and every key that went down since the last sample, released or not.
    internal static unsafe DeviceSnapshot Sample(InputEvents events)
    {
        int count;
        byte* state = Sdl3.SDL_GetKeyboardState(&count);
        ReadOnlySpan<byte> held = state == null ? default : new ReadOnlySpan<byte>(state, count);
        ReadOnlySpan<bool> pressed = events.KeysPressed;
        DeviceSnapshot snapshot = DeviceSnapshot.Empty;

        // From 1, because index 0 is Key.None and not a snapshot member.
        for (int index = 1; index < ScancodeByKey.Length; index++)
        {
            int code = (int)ScancodeByKey[index];
            if ((code < held.Length && held[code] != 0) || pressed[code])
            {
                snapshot = snapshot.With((Key)index);
            }
        }

        return snapshot;
    }

    // Writes into characters, indexed by key, what the active layout prints on each key's position,
    // uppercased, and null for a key it prints no character on. Allocates, and runs only when the
    // layout changes.
    internal static unsafe void ReadLayout(string?[] characters)
    {
        for (int index = 1; index < ScancodeByKey.Length && index < characters.Length; index++)
        {
            uint keycode = Sdl3.SDL_GetKeyFromScancode(ScancodeByKey[index], Sdl3.KeymodNone, keyEvent: false);
            characters[index] = PrintsCharacter(keycode)
                ? Marshal.PtrToStringUTF8((nint)Sdl3.SDL_GetKeyName(keycode)) is { Length: > 0 } name ? name.ToUpperInvariant() : null
                : null;
        }
    }

    // Whether an SDL keycode is a character the key prints. Space, the control characters and Delete
    // print none, and neither does a keycode that names a scancode or an extended key.
    internal static bool PrintsCharacter(uint keycode) =>
        (keycode & (Sdl3.KeycodeScancodeMask | Sdl3.KeycodeExtendedMask)) == 0 && keycode > ' ' && keycode != 0x7F;

    private static Scancode[] BuildLookup()
    {
        // GetValues sorts by value. The last key is the highest.
        Key[] keys = Enum.GetValues<Key>();
        Scancode[] lookup = new Scancode[(int)keys[^1] + 1];
        foreach (Key key in keys)
        {
            lookup[(int)key] = ToScancode(key);
        }

        return lookup;
    }

    // No discard arm: adding a Key without a mapping must fail the build (CS8509).
#pragma warning disable CS8524 // Key has no unnamed values; only a cast can produce one.
    private static Scancode ToScancode(Key key) => key switch
    {
        Key.None => Scancode.Unknown,
        Key.A => Scancode.A,
        Key.B => Scancode.B,
        Key.C => Scancode.C,
        Key.D => Scancode.D,
        Key.E => Scancode.E,
        Key.F => Scancode.F,
        Key.G => Scancode.G,
        Key.H => Scancode.H,
        Key.I => Scancode.I,
        Key.J => Scancode.J,
        Key.K => Scancode.K,
        Key.L => Scancode.L,
        Key.M => Scancode.M,
        Key.N => Scancode.N,
        Key.O => Scancode.O,
        Key.P => Scancode.P,
        Key.Q => Scancode.Q,
        Key.R => Scancode.R,
        Key.S => Scancode.S,
        Key.T => Scancode.T,
        Key.U => Scancode.U,
        Key.V => Scancode.V,
        Key.W => Scancode.W,
        Key.X => Scancode.X,
        Key.Y => Scancode.Y,
        Key.Z => Scancode.Z,

        Key.Digit0 => Scancode.Digit0,
        Key.Digit1 => Scancode.Digit1,
        Key.Digit2 => Scancode.Digit2,
        Key.Digit3 => Scancode.Digit3,
        Key.Digit4 => Scancode.Digit4,
        Key.Digit5 => Scancode.Digit5,
        Key.Digit6 => Scancode.Digit6,
        Key.Digit7 => Scancode.Digit7,
        Key.Digit8 => Scancode.Digit8,
        Key.Digit9 => Scancode.Digit9,

        Key.Grave => Scancode.Grave,

        Key.Left => Scancode.Left,
        Key.Right => Scancode.Right,
        Key.Up => Scancode.Up,
        Key.Down => Scancode.Down,

        Key.Escape => Scancode.Escape,
        Key.Enter => Scancode.Return,
        Key.Space => Scancode.Space,
        Key.Tab => Scancode.Tab,
        Key.Backspace => Scancode.Backspace,

        Key.LeftShift => Scancode.LeftShift,
        Key.RightShift => Scancode.RightShift,
        Key.LeftControl => Scancode.LeftControl,
        Key.RightControl => Scancode.RightControl,
        Key.LeftAlt => Scancode.LeftAlt,
        Key.RightAlt => Scancode.RightAlt,

        Key.F1 => Scancode.F1,
        Key.F2 => Scancode.F2,
        Key.F3 => Scancode.F3,
        Key.F4 => Scancode.F4,
        Key.F5 => Scancode.F5,
        Key.F6 => Scancode.F6,
        Key.F7 => Scancode.F7,
        Key.F8 => Scancode.F8,
        Key.F9 => Scancode.F9,
        Key.F10 => Scancode.F10,
        Key.F11 => Scancode.F11,
        Key.F12 => Scancode.F12,

        Key.Minus => Scancode.Minus,
        Key.Equals => Scancode.Equals,
        Key.LeftBracket => Scancode.LeftBracket,
        Key.RightBracket => Scancode.RightBracket,
        Key.Backslash => Scancode.Backslash,
        Key.Semicolon => Scancode.Semicolon,
        Key.Apostrophe => Scancode.Apostrophe,
        Key.Comma => Scancode.Comma,
        Key.Period => Scancode.Period,
        Key.Slash => Scancode.Slash,

        Key.Insert => Scancode.Insert,
        Key.Delete => Scancode.Delete,
        Key.Home => Scancode.Home,
        Key.End => Scancode.End,
        Key.PageUp => Scancode.PageUp,
        Key.PageDown => Scancode.PageDown,

        Key.CapsLock => Scancode.CapsLock,
        Key.NumLock => Scancode.NumLockClear,
        Key.ScrollLock => Scancode.ScrollLock,
        Key.PrintScreen => Scancode.PrintScreen,
        Key.Pause => Scancode.Pause,

        Key.LeftMeta => Scancode.LeftGui,
        Key.RightMeta => Scancode.RightGui,
        Key.Menu => Scancode.Application,

        Key.Numpad0 => Scancode.Keypad0,
        Key.Numpad1 => Scancode.Keypad1,
        Key.Numpad2 => Scancode.Keypad2,
        Key.Numpad3 => Scancode.Keypad3,
        Key.Numpad4 => Scancode.Keypad4,
        Key.Numpad5 => Scancode.Keypad5,
        Key.Numpad6 => Scancode.Keypad6,
        Key.Numpad7 => Scancode.Keypad7,
        Key.Numpad8 => Scancode.Keypad8,
        Key.Numpad9 => Scancode.Keypad9,
        Key.NumpadDivide => Scancode.KeypadDivide,
        Key.NumpadMultiply => Scancode.KeypadMultiply,
        Key.NumpadSubtract => Scancode.KeypadMinus,
        Key.NumpadAdd => Scancode.KeypadPlus,
        Key.NumpadDecimal => Scancode.KeypadPeriod,
    };
#pragma warning restore CS8524
}

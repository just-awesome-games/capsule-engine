using Capsule.Input;
using Microsoft.Xna.Framework.Input;

namespace Capsule.Runtime.Input;

// Turns the OS keyboard into a DeviceSnapshot. The only place hardware enters the engine.
internal static class KeyboardSampler
{
    private static readonly Keys[] XnaByKey = BuildLookup();

    internal static DeviceSnapshot Sample()
    {
        KeyboardState keyboard = Keyboard.GetState();
        DeviceSnapshot snapshot = DeviceSnapshot.Empty;

        // From 1: index 0 is Key.None, which is never a snapshot member.
        for (int index = 1; index < XnaByKey.Length; index++)
        {
            if (keyboard.IsKeyDown(XnaByKey[index]))
            {
                snapshot = snapshot.With((Key)index);
            }
        }

        return snapshot;
    }

    private static Keys[] BuildLookup()
    {
        Key[] keys = Enum.GetValues<Key>();

        int length = 0;
        foreach (Key key in keys)
        {
            length = Math.Max(length, (int)key + 1);
        }

        Keys[] lookup = new Keys[length];
        foreach (Key key in keys)
        {
            lookup[(int)key] = ToXna(key);
        }

        return lookup;
    }

    // No discard arm: adding a Key without a mapping must fail the build (CS8509).
#pragma warning disable CS8524 // Key has no unnamed values; only a cast can produce one.
    private static Keys ToXna(Key key) => key switch
    {
        Key.None => Keys.None,

        Key.A => Keys.A,
        Key.B => Keys.B,
        Key.C => Keys.C,
        Key.D => Keys.D,
        Key.E => Keys.E,
        Key.F => Keys.F,
        Key.G => Keys.G,
        Key.H => Keys.H,
        Key.I => Keys.I,
        Key.J => Keys.J,
        Key.K => Keys.K,
        Key.L => Keys.L,
        Key.M => Keys.M,
        Key.N => Keys.N,
        Key.O => Keys.O,
        Key.P => Keys.P,
        Key.Q => Keys.Q,
        Key.R => Keys.R,
        Key.S => Keys.S,
        Key.T => Keys.T,
        Key.U => Keys.U,
        Key.V => Keys.V,
        Key.W => Keys.W,
        Key.X => Keys.X,
        Key.Y => Keys.Y,
        Key.Z => Keys.Z,

        Key.Digit0 => Keys.D0,
        Key.Digit1 => Keys.D1,
        Key.Digit2 => Keys.D2,
        Key.Digit3 => Keys.D3,
        Key.Digit4 => Keys.D4,
        Key.Digit5 => Keys.D5,
        Key.Digit6 => Keys.D6,
        Key.Digit7 => Keys.D7,
        Key.Digit8 => Keys.D8,
        Key.Digit9 => Keys.D9,

        Key.Left => Keys.Left,
        Key.Right => Keys.Right,
        Key.Up => Keys.Up,
        Key.Down => Keys.Down,

        Key.Escape => Keys.Escape,
        Key.Enter => Keys.Enter,
        Key.Space => Keys.Space,
        Key.Tab => Keys.Tab,
        Key.Backspace => Keys.Back,

        Key.LeftShift => Keys.LeftShift,
        Key.RightShift => Keys.RightShift,
        Key.LeftControl => Keys.LeftControl,
        Key.RightControl => Keys.RightControl,
        Key.LeftAlt => Keys.LeftAlt,
        Key.RightAlt => Keys.RightAlt,

        Key.F1 => Keys.F1,
        Key.F2 => Keys.F2,
        Key.F3 => Keys.F3,
        Key.F4 => Keys.F4,
        Key.F5 => Keys.F5,
        Key.F6 => Keys.F6,
        Key.F7 => Keys.F7,
        Key.F8 => Keys.F8,
        Key.F9 => Keys.F9,
        Key.F10 => Keys.F10,
        Key.F11 => Keys.F11,
        Key.F12 => Keys.F12,
    };
#pragma warning restore CS8524
}

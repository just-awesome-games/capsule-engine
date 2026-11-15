namespace Capsule.Input;

// The engine's English name for every button on every pad family, and the path in a game's string tables
// that overrides each. The run's labels read it, and the build checks the reserved group against it.
internal static class ButtonNames
{
    // The reserved group.
    internal const string Group = "buttons";

    // The override path and English of an action with no button anywhere.
    private static readonly (string Path, string English) None = (Group + ".none", "Unbound");

    // Each group under the reserved one: by enum value, each member's override path and English, None's empty.
    private static readonly (string Path, string English)[] Keys = Section<Key>("keys", KeyName);
    private static readonly (string Path, string English)[] Mouse = Section<MouseButton>("mouse", MouseName);
    private static readonly (string Path, string English)[] Sticks = Section<StickDirection>("sticks", StickName);
    private static readonly (string Path, string English)[] Xbox = Section<PadButton>("xbox", XboxName);
    private static readonly (string Path, string English)[] PlayStation = Section<PadButton>("playStation", PlayStationName);
    private static readonly (string Path, string English)[] Nintendo = Section<PadButton>("nintendo", NintendoName);

    // Every override path a game may define.
    internal static IEnumerable<string> Paths =>
        Keys.Concat(Mouse).Concat(Sticks).Concat(Xbox).Concat(PlayStation).Concat(Nintendo).Append(None)
            .Select(static name => name.Path).Where(static path => path is not null);

    // The override path and English name of button on a pad of family, or of no button for None.
    internal static (string Path, string English) Of(InputButton button, PadFamily family)
    {
        (string Path, string English)[] section = button.Kind switch
        {
            InputButton.Source.Key => Keys,
            InputButton.Source.MouseButton => Mouse,
            InputButton.Source.StickDirection => Sticks,
            InputButton.Source.PadButton => family switch
            {
                PadFamily.PlayStation => PlayStation,
                PadFamily.Nintendo => Nintendo,
                _ => Xbox,
            },
            _ => [],
        };

        return (uint)button.Value < (uint)section.Length && section[button.Value].Path is not null ? section[button.Value] : None;
    }

    // Each member's path, its name with the first letter lowered under group, and its English, by value.
    private static (string Path, string English)[] Section<T>(string group, Func<T, string> english)
        where T : struct, Enum
    {
        T[] values = Enum.GetValues<T>();
        (string Path, string English)[] names = new (string, string)[Convert.ToInt32(values[^1], null) + 1];
        foreach (T value in values[1..])
        {
            string member = value.ToString();
            names[Convert.ToInt32(value, null)] = ($"{Group}.{group}.{char.ToLowerInvariant(member[0])}{member[1..]}", english(value));
        }

        return names;
    }

    // A key with a character is named for what a US layout prints on it. The character the player's own
    // layout prints wins over this name.
    private static string KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.Digit0 and <= Key.Digit9 => ((char)('0' + (key - Key.Digit0))).ToString(),
        Key.Left => "Left Arrow",
        Key.Right => "Right Arrow",
        Key.Up => "Up Arrow",
        Key.Down => "Down Arrow",
        Key.Escape => "Escape",
        Key.Enter => "Enter",
        Key.Space => "Space",
        Key.Tab => "Tab",
        Key.Backspace => "Backspace",
        Key.LeftShift => "Left Shift",
        Key.RightShift => "Right Shift",
        Key.LeftControl => "Left Ctrl",
        Key.RightControl => "Right Ctrl",
        Key.LeftAlt => "Left Alt",
        Key.RightAlt => "Right Alt",
        >= Key.F1 and <= Key.F12 => key.ToString(),
        Key.Grave => "`",
        Key.Minus => "-",
        Key.Equals => "=",
        Key.LeftBracket => "[",
        Key.RightBracket => "]",
        Key.Backslash => "\\",
        Key.Semicolon => ";",
        Key.Apostrophe => "'",
        Key.Comma => ",",
        Key.Period => ".",
        Key.Slash => "/",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "Page Up",
        Key.PageDown => "Page Down",
        Key.CapsLock => "Caps Lock",
        Key.NumLock => "Num Lock",
        Key.ScrollLock => "Scroll Lock",
        Key.PrintScreen => "Print Screen",
        Key.Pause => "Pause",
        Key.LeftMeta => "Left Meta",
        Key.RightMeta => "Right Meta",
        Key.Menu => "Menu",
        >= Key.Numpad0 and <= Key.Numpad9 => "Num " + (char)('0' + (key - Key.Numpad0)),
        Key.NumpadDivide => "Num /",
        Key.NumpadMultiply => "Num *",
        Key.NumpadSubtract => "Num -",
        Key.NumpadAdd => "Num +",
        Key.NumpadDecimal => "Num .",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "The key has no English name. Name it here."),
    };

    private static string MouseName(MouseButton button) => button switch
    {
        MouseButton.Left => "Left Mouse Button",
        MouseButton.Right => "Right Mouse Button",
        MouseButton.Middle => "Middle Mouse Button",
        MouseButton.X1 => "Mouse 4",
        MouseButton.X2 => "Mouse 5",
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "The mouse button has no English name. Name it here."),
    };

    private static string StickName(StickDirection direction) => direction switch
    {
        StickDirection.LeftStickUp => "Left Stick Up",
        StickDirection.LeftStickDown => "Left Stick Down",
        StickDirection.LeftStickLeft => "Left Stick Left",
        StickDirection.LeftStickRight => "Left Stick Right",
        StickDirection.RightStickUp => "Right Stick Up",
        StickDirection.RightStickDown => "Right Stick Down",
        StickDirection.RightStickLeft => "Right Stick Left",
        StickDirection.RightStickRight => "Right Stick Right",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "The stick direction has no English name. Name it here."),
    };

    // A generic pad prints what an Xbox pad does.
    private static string XboxName(PadButton button) => button switch
    {
        PadButton.DPadUp => "D-Pad Up",
        PadButton.DPadDown => "D-Pad Down",
        PadButton.DPadLeft => "D-Pad Left",
        PadButton.DPadRight => "D-Pad Right",
        PadButton.South => "A",
        PadButton.East => "B",
        PadButton.West => "X",
        PadButton.North => "Y",
        PadButton.LeftShoulder => "LB",
        PadButton.RightShoulder => "RB",
        PadButton.LeftTrigger => "LT",
        PadButton.RightTrigger => "RT",
        PadButton.LeftStickClick => "LS",
        PadButton.RightStickClick => "RS",
        PadButton.Start => "Menu",
        PadButton.Select => "View",
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "The pad button has no Xbox name. Name it here."),
    };

    private static string PlayStationName(PadButton button) => button switch
    {
        PadButton.DPadUp => "Up Button",
        PadButton.DPadDown => "Down Button",
        PadButton.DPadLeft => "Left Button",
        PadButton.DPadRight => "Right Button",
        PadButton.South => "Cross",
        PadButton.East => "Circle",
        PadButton.West => "Square",
        PadButton.North => "Triangle",
        PadButton.LeftShoulder => "L1",
        PadButton.RightShoulder => "R1",
        PadButton.LeftTrigger => "L2",
        PadButton.RightTrigger => "R2",
        PadButton.LeftStickClick => "L3",
        PadButton.RightStickClick => "R3",
        PadButton.Start => "Options",
        PadButton.Select => "Create",
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "The pad button has no PlayStation name. Name it here."),
    };

    private static string NintendoName(PadButton button) => button switch
    {
        PadButton.DPadUp => "+Control Pad Up",
        PadButton.DPadDown => "+Control Pad Down",
        PadButton.DPadLeft => "+Control Pad Left",
        PadButton.DPadRight => "+Control Pad Right",
        PadButton.South => "B",
        PadButton.East => "A",
        PadButton.West => "Y",
        PadButton.North => "X",
        PadButton.LeftShoulder => "L",
        PadButton.RightShoulder => "R",
        PadButton.LeftTrigger => "ZL",
        PadButton.RightTrigger => "ZR",
        PadButton.LeftStickClick => "L Stick",
        PadButton.RightStickClick => "R Stick",
        PadButton.Start => "+",
        PadButton.Select => "-",
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "The pad button has no Nintendo name. Name it here."),
    };
}

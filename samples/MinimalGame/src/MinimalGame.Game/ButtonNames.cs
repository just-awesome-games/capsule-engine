namespace MinimalGame.Game;

/// <summary>
/// The name of each button a prompt shows in text, in the run's language: every button that prints no
/// character and that <see cref="GamePrompts"/> has no art for on the pad family it is read for.
/// </summary>
public static class ButtonNames
{
    private static readonly Dictionary<InputButton, TextKey> Plain = new()
    {
        [Key.Left] = CapsuleStrings.Buttons.Keys.Left,
        [Key.Right] = CapsuleStrings.Buttons.Keys.Right,
        [Key.Up] = CapsuleStrings.Buttons.Keys.Up,
        [Key.Down] = CapsuleStrings.Buttons.Keys.Down,
        [Key.Tab] = CapsuleStrings.Buttons.Keys.Tab,
        [Key.Backspace] = CapsuleStrings.Buttons.Keys.Backspace,
        [Key.LeftShift] = CapsuleStrings.Buttons.Keys.LeftShift,
        [Key.RightShift] = CapsuleStrings.Buttons.Keys.RightShift,
        [Key.LeftControl] = CapsuleStrings.Buttons.Keys.LeftControl,
        [Key.RightControl] = CapsuleStrings.Buttons.Keys.RightControl,
        [Key.LeftAlt] = CapsuleStrings.Buttons.Keys.LeftAlt,
        [Key.RightAlt] = CapsuleStrings.Buttons.Keys.RightAlt,
        [Key.F1] = CapsuleStrings.Buttons.Keys.F1,
        [Key.F2] = CapsuleStrings.Buttons.Keys.F2,
        [Key.F3] = CapsuleStrings.Buttons.Keys.F3,
        [Key.F4] = CapsuleStrings.Buttons.Keys.F4,
        [Key.F5] = CapsuleStrings.Buttons.Keys.F5,
        [Key.F6] = CapsuleStrings.Buttons.Keys.F6,
        [Key.F7] = CapsuleStrings.Buttons.Keys.F7,
        [Key.F8] = CapsuleStrings.Buttons.Keys.F8,
        [Key.F9] = CapsuleStrings.Buttons.Keys.F9,
        [Key.F10] = CapsuleStrings.Buttons.Keys.F10,
        [Key.F11] = CapsuleStrings.Buttons.Keys.F11,
        [Key.F12] = CapsuleStrings.Buttons.Keys.F12,
        [Key.Insert] = CapsuleStrings.Buttons.Keys.Insert,
        [Key.Delete] = CapsuleStrings.Buttons.Keys.Delete,
        [Key.Home] = CapsuleStrings.Buttons.Keys.Home,
        [Key.End] = CapsuleStrings.Buttons.Keys.End,
        [Key.PageUp] = CapsuleStrings.Buttons.Keys.PageUp,
        [Key.PageDown] = CapsuleStrings.Buttons.Keys.PageDown,
        [Key.CapsLock] = CapsuleStrings.Buttons.Keys.CapsLock,
        [Key.NumLock] = CapsuleStrings.Buttons.Keys.NumLock,
        [Key.ScrollLock] = CapsuleStrings.Buttons.Keys.ScrollLock,
        [Key.PrintScreen] = CapsuleStrings.Buttons.Keys.PrintScreen,
        [Key.Pause] = CapsuleStrings.Buttons.Keys.Pause,
        [Key.LeftMeta] = CapsuleStrings.Buttons.Keys.LeftMeta,
        [Key.RightMeta] = CapsuleStrings.Buttons.Keys.RightMeta,
        [Key.Menu] = CapsuleStrings.Buttons.Keys.Menu,
        [Key.Numpad0] = CapsuleStrings.Buttons.Keys.Numpad0,
        [Key.Numpad1] = CapsuleStrings.Buttons.Keys.Numpad1,
        [Key.Numpad2] = CapsuleStrings.Buttons.Keys.Numpad2,
        [Key.Numpad3] = CapsuleStrings.Buttons.Keys.Numpad3,
        [Key.Numpad4] = CapsuleStrings.Buttons.Keys.Numpad4,
        [Key.Numpad5] = CapsuleStrings.Buttons.Keys.Numpad5,
        [Key.Numpad6] = CapsuleStrings.Buttons.Keys.Numpad6,
        [Key.Numpad7] = CapsuleStrings.Buttons.Keys.Numpad7,
        [Key.Numpad8] = CapsuleStrings.Buttons.Keys.Numpad8,
        [Key.Numpad9] = CapsuleStrings.Buttons.Keys.Numpad9,
        [Key.NumpadDivide] = CapsuleStrings.Buttons.Keys.NumpadDivide,
        [Key.NumpadMultiply] = CapsuleStrings.Buttons.Keys.NumpadMultiply,
        [Key.NumpadSubtract] = CapsuleStrings.Buttons.Keys.NumpadSubtract,
        [Key.NumpadAdd] = CapsuleStrings.Buttons.Keys.NumpadAdd,
        [Key.NumpadDecimal] = CapsuleStrings.Buttons.Keys.NumpadDecimal,
        [MouseButton.Middle] = CapsuleStrings.Buttons.Mouse.Middle,
        [MouseButton.X1] = CapsuleStrings.Buttons.Mouse.X1,
        [MouseButton.X2] = CapsuleStrings.Buttons.Mouse.X2,
        [StickDirection.LeftStickUp] = CapsuleStrings.Buttons.Sticks.LeftStickUp,
        [StickDirection.LeftStickDown] = CapsuleStrings.Buttons.Sticks.LeftStickDown,
        [StickDirection.LeftStickLeft] = CapsuleStrings.Buttons.Sticks.LeftStickLeft,
        [StickDirection.LeftStickRight] = CapsuleStrings.Buttons.Sticks.LeftStickRight,
        [StickDirection.RightStickUp] = CapsuleStrings.Buttons.Sticks.RightStickUp,
        [StickDirection.RightStickDown] = CapsuleStrings.Buttons.Sticks.RightStickDown,
        [StickDirection.RightStickLeft] = CapsuleStrings.Buttons.Sticks.RightStickLeft,
        [StickDirection.RightStickRight] = CapsuleStrings.Buttons.Sticks.RightStickRight,
    };

    // A generic pad prints what an Xbox pad does.
    private static readonly Dictionary<InputButton, TextKey> Xbox = new()
    {
        [PadButton.DPadUp] = CapsuleStrings.Buttons.Xbox.DPadUp,
        [PadButton.DPadDown] = CapsuleStrings.Buttons.Xbox.DPadDown,
        [PadButton.DPadLeft] = CapsuleStrings.Buttons.Xbox.DPadLeft,
        [PadButton.DPadRight] = CapsuleStrings.Buttons.Xbox.DPadRight,
        [PadButton.LeftShoulder] = CapsuleStrings.Buttons.Xbox.LeftShoulder,
        [PadButton.RightShoulder] = CapsuleStrings.Buttons.Xbox.RightShoulder,
        [PadButton.LeftTrigger] = CapsuleStrings.Buttons.Xbox.LeftTrigger,
        [PadButton.RightTrigger] = CapsuleStrings.Buttons.Xbox.RightTrigger,
        [PadButton.LeftStickClick] = CapsuleStrings.Buttons.Xbox.LeftStickClick,
        [PadButton.RightStickClick] = CapsuleStrings.Buttons.Xbox.RightStickClick,
        [PadButton.Select] = CapsuleStrings.Buttons.Xbox.Select,
    };

    private static readonly Dictionary<InputButton, TextKey> PlayStation = new()
    {
        [PadButton.DPadUp] = CapsuleStrings.Buttons.PlayStation.DPadUp,
        [PadButton.DPadDown] = CapsuleStrings.Buttons.PlayStation.DPadDown,
        [PadButton.DPadLeft] = CapsuleStrings.Buttons.PlayStation.DPadLeft,
        [PadButton.DPadRight] = CapsuleStrings.Buttons.PlayStation.DPadRight,
        [PadButton.South] = CapsuleStrings.Buttons.PlayStation.South,
        [PadButton.East] = CapsuleStrings.Buttons.PlayStation.East,
        [PadButton.West] = CapsuleStrings.Buttons.PlayStation.West,
        [PadButton.North] = CapsuleStrings.Buttons.PlayStation.North,
        [PadButton.LeftShoulder] = CapsuleStrings.Buttons.PlayStation.LeftShoulder,
        [PadButton.RightShoulder] = CapsuleStrings.Buttons.PlayStation.RightShoulder,
        [PadButton.LeftTrigger] = CapsuleStrings.Buttons.PlayStation.LeftTrigger,
        [PadButton.RightTrigger] = CapsuleStrings.Buttons.PlayStation.RightTrigger,
        [PadButton.LeftStickClick] = CapsuleStrings.Buttons.PlayStation.LeftStickClick,
        [PadButton.RightStickClick] = CapsuleStrings.Buttons.PlayStation.RightStickClick,
        [PadButton.Start] = CapsuleStrings.Buttons.PlayStation.Start,
        [PadButton.Select] = CapsuleStrings.Buttons.PlayStation.Select,
    };

    private static readonly Dictionary<InputButton, TextKey> Nintendo = new()
    {
        [PadButton.DPadUp] = CapsuleStrings.Buttons.Nintendo.DPadUp,
        [PadButton.DPadDown] = CapsuleStrings.Buttons.Nintendo.DPadDown,
        [PadButton.DPadLeft] = CapsuleStrings.Buttons.Nintendo.DPadLeft,
        [PadButton.DPadRight] = CapsuleStrings.Buttons.Nintendo.DPadRight,
        [PadButton.South] = CapsuleStrings.Buttons.Nintendo.South,
        [PadButton.East] = CapsuleStrings.Buttons.Nintendo.East,
        [PadButton.West] = CapsuleStrings.Buttons.Nintendo.West,
        [PadButton.North] = CapsuleStrings.Buttons.Nintendo.North,
        [PadButton.LeftShoulder] = CapsuleStrings.Buttons.Nintendo.LeftShoulder,
        [PadButton.RightShoulder] = CapsuleStrings.Buttons.Nintendo.RightShoulder,
        [PadButton.LeftTrigger] = CapsuleStrings.Buttons.Nintendo.LeftTrigger,
        [PadButton.RightTrigger] = CapsuleStrings.Buttons.Nintendo.RightTrigger,
        [PadButton.LeftStickClick] = CapsuleStrings.Buttons.Nintendo.LeftStickClick,
        [PadButton.RightStickClick] = CapsuleStrings.Buttons.Nintendo.RightStickClick,
        [PadButton.Start] = CapsuleStrings.Buttons.Nintendo.Start,
        [PadButton.Select] = CapsuleStrings.Buttons.Nintendo.Select,
    };

    /// <summary>The name of <paramref name="button"/> on a pad of <paramref name="family"/>, or false where it has none.</summary>
    public static bool TryGet(InputButton button, PadFamily family, out TextKey name) =>
        Plain.TryGetValue(button, out name) || PadNames(family).TryGetValue(button, out name);

    private static Dictionary<InputButton, TextKey> PadNames(PadFamily family) => family switch
    {
        PadFamily.PlayStation => PlayStation,
        PadFamily.Nintendo => Nintendo,
        _ => Xbox,
    };
}

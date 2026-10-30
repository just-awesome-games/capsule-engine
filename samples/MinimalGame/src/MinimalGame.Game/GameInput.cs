namespace MinimalGame.Game;

/// <summary>
/// The game's actions, and the one place devices are named. An action is the seam between a device
/// and the logic that reacts to it: <see cref="MinimalGameInstance"/> installs this configuration
/// once as the run starts, and everything else in the game reads actions, never keys or pad buttons.
/// </summary>
public static class GameInput
{
    /// <summary>Horizontal movement, in [-1, 1].</summary>
    public static readonly AxisAction Move = new("move");

    /// <summary>Leaves the floor.</summary>
    public static readonly InputAction Jump = new("jump");

    /// <summary>Held with Jump to drop through a one-way ledge or platform.</summary>
    public static readonly InputAction Drop = new("drop");

    /// <summary>Fires a bolt from the muzzle, in the direction the player faces.</summary>
    public static readonly InputAction Shoot = new("shoot");

    /// <summary>Moves the menu focus to the item above.</summary>
    public static readonly InputAction MenuUp = new("menu-up");

    /// <summary>Moves the menu focus to the item below.</summary>
    public static readonly InputAction MenuDown = new("menu-down");

    /// <summary>Moves the menu focus to the item to the left.</summary>
    public static readonly InputAction MenuLeft = new("menu-left");

    /// <summary>Moves the menu focus to the item to the right.</summary>
    public static readonly InputAction MenuRight = new("menu-right");

    /// <summary>Presses the focused menu item, or the one under the pointer when clicked.</summary>
    public static readonly InputAction Confirm = new("confirm");

    /// <summary>Leaves the game from the title screen.</summary>
    public static readonly InputAction Quit = new("quit");

    /// <summary>Opens the pause menu in play, or closes it.</summary>
    public static readonly InputAction Pause = new("pause");

    /// <summary>Cancels a rebinding capture, or leaves the options screen.</summary>
    public static readonly InputAction Back = new("back");

    /// <summary>Returns the options row it presses to its default.</summary>
    public static readonly InputAction Reset = new("reset");

    /// <summary>
    /// What drives a menu's focus, declared here beside the actions it names so every menu the game
    /// opens is navigated the same way.
    /// </summary>
    public static readonly FocusActions MenuFocus =
        new(MenuUp, MenuDown, MenuLeft, MenuRight, Confirm, Cancel: Back, Secondary: [Reset]);

    /// <summary>Binds every action to the devices the game supports.</summary>
    public static void Configure(InputConfiguration input, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);

        // Axis contributions accumulate. Each pair adds another way to push the same axis.
        input.Bind(Move, Key.A, Key.D);
        input.Bind(Move, Key.Left, Key.Right);
        input.Bind(Move, PadButton.DPadLeft, PadButton.DPadRight);
        input.Bind(Move, PadAxis.LeftStickX);

        input.Bind(MenuUp, Key.Up, Key.W, PadButton.DPadUp, StickDirection.LeftStickUp);
        input.Bind(MenuDown, Key.Down, Key.S, PadButton.DPadDown, StickDirection.LeftStickDown);

        // The same keys MenuDown takes: menus read MenuDown and play reads Drop, never both at once.
        input.Bind(Drop, Key.Down, Key.S, PadButton.DPadDown, StickDirection.LeftStickDown);

        // The same keys the Move axis takes. One device can serve either because no scene reads both.
        input.Bind(MenuLeft, Key.Left, Key.A, PadButton.DPadLeft, StickDirection.LeftStickLeft);
        input.Bind(MenuRight, Key.Right, Key.D, PadButton.DPadRight, StickDirection.LeftStickRight);
        input.Bind(Confirm, Key.Enter, Key.Space, PadButton.South, MouseButton.Left);
        input.Bind(Reset, Key.Delete, Key.Backspace, PadButton.North, MouseButton.Right);

        // Quit, Pause and Back share Escape. The title screen reads Quit, play reads Pause and the
        // options screen reads Back, and no scene reads two of them.
        input.Bind(Quit, Key.Escape, PadButton.Start);
        input.Bind(Pause, Key.Escape, PadButton.Start);
        input.Bind(Back, Key.Escape, PadButton.East);

        // Jump and Shoot are the player's. Their defaults sit on InputSettings, and a saved document
        // replaces them before this runs.
        input.Bind(Jump, settings.Input.Jump.Key, settings.Input.Jump.Pad);
        input.Bind(Shoot, settings.Input.Shoot.Key, settings.Input.Shoot.Pad);
    }
}

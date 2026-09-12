using Capsule.Input;
using Capsule.Scenes.Input;

namespace MinimalGame.Game;

/// <summary>
/// The game's actions, and the one place devices are named. An action is the seam between a device
/// and the logic that reacts to it: the shell installs this configuration once through
/// <c>WithInput</c>, and everything else in the game reads actions, never keys or pad buttons.
/// </summary>
public static class GameInput
{
    /// <summary>Horizontal movement, in [-1, 1].</summary>
    public static readonly AxisAction Move = new("move");

    /// <summary>Leaves the floor.</summary>
    public static readonly InputAction Jump = new("jump");

    /// <summary>Moves the menu focus to the item above.</summary>
    public static readonly InputAction MenuUp = new("menu-up");

    /// <summary>Moves the menu focus to the item below.</summary>
    public static readonly InputAction MenuDown = new("menu-down");

    /// <summary>Moves the menu focus to the item to the left.</summary>
    public static readonly InputAction MenuLeft = new("menu-left");

    /// <summary>Moves the menu focus to the item to the right.</summary>
    public static readonly InputAction MenuRight = new("menu-right");

    /// <summary>Accepts the menu.</summary>
    public static readonly InputAction Confirm = new("confirm");

    /// <summary>Picks the menu item under the pointer.</summary>
    public static readonly InputAction Click = new("click");

    /// <summary>Leaves the game.</summary>
    public static readonly InputAction Quit = new("quit");

    /// <summary>
    /// What drives a menu's focus, declared here beside the actions it names so every menu the game
    /// opens is navigated the same way.
    /// </summary>
    public static readonly FocusActions MenuFocus = new(MenuUp, MenuDown, MenuLeft, MenuRight, Confirm, Click);

    /// <summary>Sets the gamepad deadzones and binds every action to the devices the game supports.</summary>
    public static void Configure(InputConfiguration input)
    {
        ArgumentNullException.ThrowIfNull(input);

        input.GamepadDeadzones(InputConfiguration.DefaultStickDeadzone, InputConfiguration.DefaultTriggerDeadzone);

        ActionBindings bindings = input.Bindings;

        // Axis contributions accumulate, so each pair adds another way to push the same axis.
        bindings.BindAxis(Move, Key.A, Key.D);
        bindings.BindAxis(Move, Key.Left, Key.Right);
        bindings.BindAxis(Move, PadButton.DPadLeft, PadButton.DPadRight);
        bindings.BindAxis(Move, PadAxis.LeftStickX);

        bindings.Bind(Jump, Key.Space, PadButton.South);
        bindings.Bind(MenuUp, Key.Up, Key.W, PadButton.DPadUp, StickDirection.LeftStickUp);
        bindings.Bind(MenuDown, Key.Down, Key.S, PadButton.DPadDown, StickDirection.LeftStickDown);

        // The same keys the Move axis takes: no scene reads both, so one device can serve either.
        bindings.Bind(MenuLeft, Key.Left, Key.A, PadButton.DPadLeft, StickDirection.LeftStickLeft);
        bindings.Bind(MenuRight, Key.Right, Key.D, PadButton.DPadRight, StickDirection.LeftStickRight);
        bindings.Bind(Confirm, Key.Enter, Key.Space, PadButton.South);

        bindings.Bind(Click, MouseButton.Left);
        bindings.Bind(Quit, Key.Escape, PadButton.Start);
    }
}

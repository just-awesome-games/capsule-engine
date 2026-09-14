using Capsule.Input;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// The overlay's actions and the one place its devices are named. The keyboard key is bound first
// on every action, so KeyName reads the keyboard name of an action.
internal static class DebugInput
{
    internal static readonly InputAction MenuUp = new("debug-menu.up");
    internal static readonly InputAction MenuDown = new("debug-menu.down");
    internal static readonly InputAction Confirm = new("debug-menu.confirm");
    internal static readonly InputAction Back = new("debug-menu.back");
    internal static readonly InputAction Step = new("debug-menu.step");
    internal static readonly InputAction Hide = new("debug-menu.hide");
    internal static readonly InputAction Restart = new("debug-menu.restart");
    internal static readonly InputAction LoadScene = new("debug-menu.load-scene");
    internal static readonly InputAction DebugDraw = new("debug-menu.debug-draw");
    internal static readonly InputAction FramePane = new("debug-menu.frame-pane");
    internal static readonly InputAction Exit = new("debug-menu.exit");
    internal static readonly InputAction Click = new("debug-menu.click");

    // Sideways moves are the menu's own (back and step), so the navigator is given a direction
    // bound to nothing.
    private static readonly InputAction None = new("debug-menu.none");

    internal static readonly FocusActions MenuFocus = new(MenuUp, MenuDown, None, None, Confirm, Click);

    internal static readonly InputAction[] Actions =
        [MenuUp, MenuDown, Confirm, Back, Step, Hide, Restart, LoadScene, DebugDraw, FramePane, Exit, Click];

    private static readonly ActionBindings Named = Bindings();

    internal static ActionBindings Bindings() =>
        new ActionBindings()
            .Bind(MenuUp, Key.Up, PadButton.DPadUp)
            .Bind(MenuDown, Key.Down, PadButton.DPadDown)
            .Bind(Confirm, Key.Enter, PadButton.South)
            .Bind(Back, Key.Backspace, Key.Left, PadButton.East, PadButton.DPadLeft)
            .Bind(Step, Key.Right, PadButton.DPadRight)
            .Bind(Hide, Key.H)
            .Bind(Restart, Key.R)
            .Bind(LoadScene, Key.L)
            .Bind(DebugDraw, Key.D)
            .Bind(FramePane, Key.F)
            .Bind(Exit, Key.E)
            .Bind(Click, MouseButton.Left);

    internal static string KeyName(InputAction action) => KeyName(Named.ButtonsFor(action)[0]);

    internal static string KeyName(InputButton button) => button == Key.Grave ? "~" : button.ToString();
}

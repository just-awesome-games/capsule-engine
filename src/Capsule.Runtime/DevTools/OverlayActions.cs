using Capsule.Input;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// The overlay's input actions and their bindings, the only place its devices are named. No behaviour.
internal static class OverlayActions
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
    internal static readonly InputAction TimeScale = new("debug-menu.time-scale");
    internal static readonly InputAction FramePane = new("debug-menu.frame-pane");
    internal static readonly InputAction ScenePage = new("debug-menu.scene");
    internal static readonly InputAction Exit = new("debug-menu.exit");
    internal static readonly InputAction Click = new("debug-menu.click");
    internal static readonly AxisAction Scroll = new("debug-menu.scroll");

    // The menu handles sideways moves itself (back and step), so the navigator is given a direction
    // bound to nothing.
    private static readonly InputAction None = new("debug-menu.none");

    internal static readonly FocusActions MenuFocus = new(MenuUp, MenuDown, None, None, Confirm, Click);

    internal static readonly InputAction[] Actions =
        [MenuUp, MenuDown, Confirm, Back, Step, Hide, Restart, LoadScene, DebugDraw, TimeScale, FramePane, ScenePage, Exit, Click];

    // Read-only once built, and a single instance serves every overlay. The keyboard key is bound first
    // on every action, because KeyName reads an action's first button as its keyboard name.
    internal static readonly ActionBindings Bindings =
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
            .Bind(TimeScale, Key.T)
            .Bind(FramePane, Key.F)
            .Bind(ScenePage, Key.S)
            .Bind(Exit, Key.E)
            .Bind(Click, MouseButton.Left)
            .BindAxis(Scroll, MouseAxis.ScrollY);

    internal static string KeyName(InputAction action) => KeyName(Bindings.ButtonsFor(action)[0]);

    internal static string KeyName(InputButton button) => button == Key.Grave ? "~" : button.Name;
}

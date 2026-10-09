using Capsule.Input;

namespace Capsule.Runtime.DevTools;

// The only place the overlay's devices are named.
internal static class OverlayActions
{
    internal static readonly InputAction MenuUp = new("debug-overlay.up");
    internal static readonly InputAction MenuDown = new("debug-overlay.down");
    internal static readonly InputAction Confirm = new("debug-overlay.confirm");
    internal static readonly InputAction Back = new("debug-overlay.back");
    internal static readonly InputAction Step = new("debug-overlay.step");
    internal static readonly InputAction Hide = new("debug-overlay.hide");
    internal static readonly InputAction Restart = new("debug-overlay.restart");
    internal static readonly InputAction LoadScene = new("debug-overlay.load-scene");
    internal static readonly InputAction DebugDraw = new("debug-overlay.debug-draw");
    internal static readonly InputAction TimeScale = new("debug-overlay.time-scale");
    internal static readonly InputAction FramePane = new("debug-overlay.frame-pane");
    internal static readonly InputAction ScenePage = new("debug-overlay.scene");
    internal static readonly InputAction GameCamera = new("debug-overlay.game-camera");
    internal static readonly InputAction Exit = new("debug-overlay.exit");
    internal static readonly InputAction Click = new("debug-overlay.click");
    internal static readonly InputAction Move = new("debug-overlay.move");

    // The free camera's. A pan is Pan held, or Grab held while Click goes down. Zoom and Sideways turn the
    // wheel into a zoom or a sideways scroll.
    internal static readonly InputAction Pan = new("debug-overlay.pan");
    internal static readonly InputAction Grab = new("debug-overlay.grab");
    internal static readonly InputAction Zoom = new("debug-overlay.zoom");
    internal static readonly InputAction Sideways = new("debug-overlay.sideways");

    internal static readonly AxisAction Scroll = new("debug-overlay.scroll");
    internal static readonly AxisAction ScrollSideways = new("debug-overlay.scroll-sideways");

    // Every button bound here is withheld from the game while the overlay holds the run.
    internal static readonly InputAction[] Actions =
    [
        MenuUp, MenuDown, Confirm, Back, Step, Hide, Restart, LoadScene, DebugDraw, TimeScale, FramePane, ScenePage, GameCamera, Exit, Click,
        Move, Pan, Grab, Zoom, Sideways,
    ];

    // Shared by every overlay. The key is bound first because KeyName reads the first button.
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
            .Bind(GameCamera, Key.C)
            .Bind(Exit, Key.E)
            .Bind(Click, MouseButton.Left)
            .Bind(Move, Key.G)
            .Bind(Pan, MouseButton.Middle)
            .Bind(Grab, Key.Space)
            .Bind(Zoom, Key.LeftControl, Key.RightControl)
            .Bind(Sideways, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt)
            .BindAxis(Scroll, MouseAxis.ScrollY)
            .BindAxis(ScrollSideways, MouseAxis.ScrollX);

    internal static string KeyName(InputAction action) => KeyName(Bindings.ButtonsFor(action)[0]);

    internal static string KeyName(InputButton button) => button == Key.Grave ? "~" : button.Name;
}

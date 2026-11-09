using Capsule.Input;

namespace Capsule.Runtime.DevTools.Overlay;

// The only place the overlay's devices are bound. The Help page names its keys from these bindings, and
// the menu's legend abbreviates them.
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
    internal static readonly InputAction SaveRecording = new("debug-overlay.save-recording");
    internal static readonly InputAction Help = new("debug-overlay.help");
    internal static readonly InputAction Click = new("debug-overlay.click");
    internal static readonly InputAction Move = new("debug-overlay.move");
    internal static readonly InputAction Frame = new("debug-overlay.frame");

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
        MenuUp, MenuDown, Confirm, Back, Step, Hide, Restart, LoadScene, DebugDraw, TimeScale, FramePane, ScenePage, GameCamera, Exit, SaveRecording, Help, Click,
        Move, Frame, Pan, Grab, Zoom, Sideways,
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
            .Bind(FramePane, Key.P)
            .Bind(ScenePage, Key.S)
            .Bind(GameCamera, Key.C)
            .Bind(Exit, Key.E)
            .Bind(SaveRecording, Key.W)
            .Bind(Help, Key.F1)
            .Bind(Click, MouseButton.Left)
            .Bind(Move, Key.G)
            .Bind(Frame, Key.F, PadButton.North)
            .Bind(Pan, MouseButton.Middle)
            .Bind(Grab, Key.Space)
            .Bind(Zoom, Key.LeftControl, Key.RightControl)
            .Bind(Sideways, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt)
            .Bind(Scroll, MouseAxis.ScrollY)
            .Bind(ScrollSideways, MouseAxis.ScrollX);

    // The Help page: each input no row shows. A line with no keys is a heading. Keys are named from the
    // bindings above, and the mouse's gestures are written out.
    internal static readonly HelpLine[] HelpLines =
    [
        new("Menu"),
        new("Move the focus", $"{Keys(MenuUp, MenuDown)}, Point"),
        new("Scroll the rows", "Wheel"),
        new("Activate the row", $"{Keys(Confirm)}, Click"),
        new("Go back", Keys(Back)),
        new("World"),
        new("Select what is drawn here, again to go deeper", "Click"),
        new("Scroll the view", "Wheel"),
        new("Scroll sideways", $"{Keys(Sideways)}+Wheel"),
        new("Zoom", $"{Keys(Zoom)}+Wheel"),
        new("Pan", $"Middle-drag, {Keys(Grab)}+left-drag"),
        new("Move the open entity here", Keys(Move)),
        new("Frame the open entity", Keys(Frame)),
        new("Hidden"),
        new("Show the menu", Keys(Hide)),
        new("Return to the game's camera", Keys(GameCamera)),
    ];

    internal static string KeyName(InputAction action) => KeyName(Bindings.ButtonsFor(action)[0]);

    // What a row shows for button: the character a US layout prints on a key, else the constant's bare
    // name, as F1. The rows are built before the player's layout is read, and the overlay is English.
    internal static string KeyName(InputButton button)
    {
        if (button.AsKey is { } key && KeyCharacters.Us[(int)key] is { } character)
        {
            return character;
        }

        string qualified = button.ToString();

        return qualified[(qualified.IndexOf('.', StringComparison.Ordinal) + 1)..];
    }

    // Every key bound to actions, apart by slashes, with a left and right pair named once, as Shift.
    private static string Keys(params InputAction[] actions)
    {
        List<string> names = [];
        foreach (InputAction action in actions)
        {
            foreach (InputButton button in Bindings.ButtonsFor(action))
            {
                if (button.AsKey is null)
                {
                    continue;
                }

                string name = KeyName(button);
                foreach (string side in (ReadOnlySpan<string>)["Left", "Right"])
                {
                    if (name.Length > side.Length && name.StartsWith(side, StringComparison.Ordinal))
                    {
                        name = name[side.Length..];
                    }
                }

                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }

        return string.Join('/', names);
    }
}

internal readonly record struct HelpLine(string Label, string? Keys = null);

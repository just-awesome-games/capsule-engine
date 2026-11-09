namespace MinimalGame.Game.UI;

/// <summary>
/// The menu over a paused room: the whole canvas dimmed and drained of colour, and a panel centred on it holding
/// Resume and Quit over one <see cref="FocusNavigator"/>. The scene opens and closes it, and the menu owns what
/// pausing means: the world, the sound effects and the rumble all stop.
/// </summary>
public sealed class PauseMenu : ScreenEntity
{
    // Canvas pixels between the panel's edge and its items.
    private const float PanelPadding = 8f;

    private static readonly ColorRgba PanelColor = new(24, 24, 32);

    private readonly MenuItem _resume = new(CapsuleStrings.Menu.Resume);
    private readonly MenuItem _quit = new(CapsuleStrings.Menu.Quit);

    private readonly FocusNavigator _navigator;

    // The room's cursor, which the menu covers with the system arrow while it is open.
    private Sprite? _roomCursor;

    public PauseMenu()
        : base(Anchor.Fill, Vector2.Zero)
    {
        // Steps only while the scene is paused. Its focus never reads the player's input.
        StepMode = StepMode.WhenPaused;
        Add(new ColorRect { Material = new(CapsuleAssets.Shaders.PauseDimShader) });

        // The panel fits its items, and its colour fills the fitted rect.
        BoxContainer panel = new(Axis.Vertical, Anchor.Center, Vector2.Zero)
        {
            Parent = this,
            Padding = new Insets(PanelPadding),
            Spacing = MenuItem.Spacing,
        };
        panel.Add(new ColorRect { Color = PanelColor });

        _resume.Parent = panel;
        _quit.Parent = panel;

        _navigator = Add(new FocusNavigator(GameInput.MenuFocus));

        _resume.Pressed += Close;
        _quit.Pressed += Quit;

        Visible = false;
    }

    /// <summary>
    /// Pauses the scene and its sound effects, stops the rumble, and shows the menu with Resume focused
    /// and the system arrow to point at it.
    /// </summary>
    public void Open()
    {
        Scene.Paused = true;
        Run.Audio.Pause(AudioBuses.Sfx);
        Run.Rumble.Stop();
        _roomCursor = Run.Cursor.Image;
        Run.Cursor.Image = null;

        // Shown first: the navigator passes the focus over an item that is hidden.
        Visible = true;
        _navigator.Focus(_resume.Focusable);
    }

    /// <summary>Resumes the scene and its sound effects, hides the menu and gives the room its cursor back.</summary>
    public void Close()
    {
        Scene.Paused = false;
        Run.Audio.Resume(AudioBuses.Sfx);
        Run.Cursor.Image = _roomCursor;
        Visible = false;
    }

    private void Quit() => Run.RequestExit();
}

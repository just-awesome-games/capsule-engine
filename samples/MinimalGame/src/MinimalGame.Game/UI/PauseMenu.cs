using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace MinimalGame.Game.UI;

/// <summary>
/// The menu over a paused room: a dim over the whole canvas, and a panel centred on it holding Resume
/// and Quit over one <see cref="FocusNavigator"/>. The scene opens and closes it, and the menu owns what
/// pausing means: the world, the sound effects and the rumble all stop.
/// </summary>
public sealed class PauseMenu : ScreenEntity
{
    // Canvas pixels between the panel's edge and its items.
    private const float PanelPadding = 8f;

    // Two items one spacing apart at their narrowest, inside the padding.
    private static readonly Vector2 PanelSize = new(MenuItem.MinWidth + (2f * PanelPadding), MenuItem.Spacing + MenuItem.BoxHeight + (2f * PanelPadding));

    private static readonly ColorRgba DimColor = new(0, 0, 0, 160);
    private static readonly ColorRgba PanelColor = new(24, 24, 32);

    private readonly MenuItem _resume = new(Anchor.Top, Vector2.Zero, "Resume");
    private readonly MenuItem _quit = new(Anchor.Top, new Vector2(0f, MenuItem.Spacing), "Quit");

    private readonly FocusNavigator _navigator;

    public PauseMenu()
        : base(Anchor.Fill, Vector2.Zero)
    {
        // Steps only while the scene is paused, so its focus never reads the player's input.
        StepMode = StepMode.WhenPaused;
        Add(new ColorRect { Color = DimColor });

        ScreenEntity panel = new(Anchor.Center, Vector2.Zero)
        {
            Parent = this,
            Size = PanelSize,
            Padding = new Insets(PanelPadding),
        };
        panel.Add(new ColorRect { Color = PanelColor });

        _resume.Parent = panel;
        _quit.Parent = panel;

        _navigator = new FocusNavigator(GameInput.MenuFocus, _resume.Focusable, _quit.Focusable);
        Add(_navigator);

        _resume.Pressed += Close;
        _quit.Pressed += Quit;

        Visible = false;
    }

    /// <summary>
    /// Pauses the scene and its sound effects, stops the rumble, and shows the menu with Resume focused.
    /// </summary>
    public void Open()
    {
        Scene.Paused = true;
        Run.Audio.Pause(AudioBuses.Sfx);
        Run.Rumble.Stop();

        // Shown first: the navigator passes the focus over an item that is hidden.
        Visible = true;
        _navigator.Focus(_resume.Focusable);
    }

    /// <summary>Resumes the scene and its sound effects and hides the menu.</summary>
    public void Close()
    {
        Scene.Paused = false;
        Run.Audio.Resume(AudioBuses.Sfx);
        Visible = false;
    }

    private void Quit() => Run.RequestExit();
}

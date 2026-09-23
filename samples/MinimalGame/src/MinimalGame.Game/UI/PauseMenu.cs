using System.Numerics;
using Capsule.Scenes;
using Capsule.UI;

namespace MinimalGame.Game.UI;

/// <summary>
/// The menu over a paused room: Resume and Quit, over one <see cref="FocusNavigator"/>.
/// The scene opens and closes it, and the menu owns what pausing means: the world, the sound effects
/// and the rumble all stop.
/// </summary>
public sealed class PauseMenu : ScreenEntity
{
    // Canvas pixels between neighbouring items' centres.
    private const float ItemSpacing = 20f;

    private readonly MenuItem _resume = new(Anchor.Center, new Vector2(0f, -0.5f * ItemSpacing), "Resume");
    private readonly MenuItem _quit = new(Anchor.Center, new Vector2(0f, 0.5f * ItemSpacing), "Quit");

    private readonly FocusNavigator _navigator;

    public PauseMenu()
        : base(Anchor.Center, Vector2.Zero)
    {
        // Steps only while the scene is paused, so its focus never reads the player's input.
        StepMode = StepMode.WhenPaused;

        _navigator = new FocusNavigator(GameInput.MenuFocus, _resume.Focusable, _quit.Focusable);
        Add(_navigator);

        _resume.Pressed += Close;
        _quit.Pressed += Quit;

        Show(false);
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
        Show(true);
        _navigator.Focus(_resume.Focusable);
    }

    /// <summary>Resumes the scene and its sound effects and hides the menu.</summary>
    public void Close()
    {
        Scene.Paused = false;
        Run.Audio.Resume(AudioBuses.Sfx);
        Show(false);
    }

    // The items are anchored to the canvas's centre rather than to this entity, so they are the
    // scene's peers, the same shape TitleMenu adds its own items in.
    /// <inheritdoc/>
    protected override void OnAddedToScene()
    {
        Scene.Add(_resume);
        Scene.Add(_quit);
    }

    private void Quit() => Run.RequestExit();

    // The items are peers rather than children, so each is shown and hidden with the menu by name.
    private void Show(bool shown)
    {
        Visible = shown;
        _resume.Visible = shown;
        _quit.Visible = shown;
    }
}

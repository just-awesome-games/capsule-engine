using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The boot scene, showing the title menu. A public parameterless constructor marks it class-only,
/// with no <c>*.scene.json</c>.
/// </summary>
public sealed class MainMenu : Scene
{
    // A menu added in the constructor puts its font in the scene's preload, which OnStart is too late for.
    public MainMenu() => Add(new TitleMenu());

    /// <inheritdoc/>
    protected override void OnStart()
    {
        // A track already playing is left alone. A trip through Options does not restart it.
        Run.Game.Music.Play(CapsuleAssets.Audio.Music.TitleSound);

        // A room's crosshair does not follow the player out.
        Run.Cursor.Image = null;

        // Start opens the room, which loads behind the menu.
        Run.PrefetchScene(CapsuleAssets.Scenes.RoomScene);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Quit))
        {
            Run.RequestExit();
        }
    }
}

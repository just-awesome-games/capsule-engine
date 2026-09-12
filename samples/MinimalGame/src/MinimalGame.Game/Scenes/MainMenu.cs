using Capsule;
using Capsule.Scenes;
using MinimalGame.Game.Entities;
using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The boot scene, and the one backed by no <c>*.scene.json</c>: its public parameterless constructor
/// is what marks it class-only. It shows the title menu and nothing else, and it instantiates it in
/// its own constructor rather than in <see cref="OnStart"/>, which is what makes the menu's font part
/// of the scene's asset preload. Quitting is the scene's, because it is the scene that is left.
/// </summary>
public sealed class MainMenu : Scene
{
    public MainMenu() => Add(new TitleMenu());

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }
}

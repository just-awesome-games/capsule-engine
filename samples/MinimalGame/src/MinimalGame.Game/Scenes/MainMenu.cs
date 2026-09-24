using Capsule;
using Capsule.Scenes;
using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The boot scene, and the one backed by no <c>*.scene.json</c>: its public parameterless constructor
/// is what marks it class-only. It shows the title menu and nothing else, and it instantiates it in
/// its own constructor rather than in <see cref="OnStart"/>, which is what makes the menu's font part
/// of the scene's asset preload.
/// </summary>
public sealed class MainMenu : Scene
{
    public MainMenu() => Add(new TitleMenu());

    // A track already playing is left alone, so a trip through Options and back does not restart it.
    // Start is the item highlighted on arrival. The room it opens loads behind the menu.
    /// <inheritdoc/>
    protected override void OnStart()
    {
        Run.Game.Music.Play(CapsuleAssets.Audio.Music.TitleSound);
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

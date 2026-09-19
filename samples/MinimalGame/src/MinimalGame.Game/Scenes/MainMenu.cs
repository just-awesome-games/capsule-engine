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

    // The saved settings are restored before the first scene composes, so the bus is levelled from the
    // document here and stands for the whole run.
    /// <inheritdoc/>
    protected override void OnStart() =>
        Run.Audio.SetVolume(AudioBuses.Sfx, Run.Saves.Read(GameSaves.Settings).SoundOn ? 1f : 0f);

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Quit))
        {
            Run.RequestExit();
        }
    }
}

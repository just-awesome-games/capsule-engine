using Capsule;

namespace MinimalGame.Game;

/// <summary>What the game does once when a run starts, before its first scene.</summary>
public static class GameBoot
{
    public static void Start(Run run)
    {
        GameSettings settings = run.Saves.Read(GameSaves.Settings);

        GameInput.Configure(run.Input, settings);
        run.Audio.SetVolume(AudioBuses.Sfx, settings.SoundOn ? 1f : 0f);
    }
}

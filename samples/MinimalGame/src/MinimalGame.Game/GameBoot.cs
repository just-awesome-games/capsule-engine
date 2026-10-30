namespace MinimalGame.Game;

/// <summary>What the game does once when a run starts, before its first scene, and once when it ends.</summary>
public static class GameBoot
{
    public static void Start(Run run)
    {
        run.Attach(new GameInstance(run));

        GameSettings settings = run.Saves.Read(GameSaves.Settings);

        // The run starts in the system's language. The player's own choice wins.
        if (settings.Language is { } language)
        {
            run.Language = language;
        }

        // The window has not opened yet. It opens as the player left it.
        settings.ApplyDisplay(run.Display);
        GameInput.Configure(run.Input, settings);
        run.Audio.SetVolume(AudioBuses.Sfx, settings.EffectsVolume);
        run.Audio.SetVolume(AudioBuses.Music, settings.MusicVolume);
        run.Display.ShakeStrength = settings.ShakeStrength;
        run.Rumble.Volume = settings.RumbleVolume;
    }

    // A save moment at quit, whichever way the player quits: the run's length is banked in the profile.
    public static void End(Run run)
    {
        GameProfile profile = run.Saves.Read(GameSaves.Profile);
        profile.PlayedSeconds += run.TotalSeconds;
        run.Saves.Write(GameSaves.Profile, profile);
    }
}

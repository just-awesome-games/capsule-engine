namespace MinimalGame.Game;

/// <summary>What the game keeps for the length of one run, and what it does once as the run starts and ends.</summary>
public sealed class MinimalGameInstance(Run run) : GameInstance(run)
{
    /// <summary>The run's one music track, crossfaded between the menu and a level.</summary>
    public Music Music { get; } = new(run.Audio);

    protected override void OnStart()
    {
        GameSettings settings = Run.Saves.Read(GameSaves.Settings);

        // The run starts in the system's language. The player's own choice wins.
        if (settings.Language is { } language)
        {
            Run.Language = language;
        }

        // The window has not opened yet. It opens as the player left it.
        settings.ApplyDisplay(Run.Display);
        GameInput.Configure(Run.Input, settings);
        Run.Audio.SetVolume(AudioBuses.Sfx, settings.EffectsVolume);
        Run.Audio.SetVolume(AudioBuses.Music, settings.MusicVolume);
        Run.Display.ShakeStrength = settings.ShakeStrength;
        Run.Rumble.Volume = settings.RumbleVolume;
    }

    // A save moment at quit, whichever way the player quits: the run's length is banked in the profile.
    protected override void OnEnd()
    {
        GameProfile profile = Run.Saves.Read(GameSaves.Profile);
        profile.PlayedSeconds += Run.TotalSeconds;
        Run.Saves.Write(GameSaves.Profile, profile);
    }
}

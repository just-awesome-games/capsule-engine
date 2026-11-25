namespace MinimalGame.Game;

public static class AudioBuses
{
    /// <summary>The bus that plays the game's sound effects, levelled from the saved settings.</summary>
    public static readonly AudioBus Sfx = new AudioBus("sfx");

    /// <summary>The bus the music's level is set on, levelled from the saved settings.</summary>
    public static readonly AudioBus Music = new AudioBus("music");

    /// <summary>The bus the music plays on, under <see cref="Music"/>, which the pause menu muffles and dips.</summary>
    public static readonly AudioBus Score = new AudioBus("score", Music);
}

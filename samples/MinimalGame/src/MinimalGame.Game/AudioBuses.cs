using Capsule.Audio;

namespace MinimalGame.Game;

public static class AudioBuses
{
    /// <summary>The bus that plays the game's music.</summary>
    public static readonly AudioBus Music = new AudioBus("music");

    /// <summary>The bus that plays the game's sound effects.</summary>
    public static readonly AudioBus Sfx = new AudioBus("sfx");
}

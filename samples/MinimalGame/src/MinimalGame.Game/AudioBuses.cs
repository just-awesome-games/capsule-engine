using Capsule.Audio;

namespace MinimalGame.Game;

public static class AudioBuses
{
    /// <summary>The bus that plays the game's sound effects, levelled from the saved settings at boot.</summary>
    public static readonly AudioBus Sfx = new AudioBus("sfx");
}

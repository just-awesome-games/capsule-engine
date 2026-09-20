using Capsule;

namespace MinimalGame.Game;

/// <summary>What the game keeps for the length of one run, attached at boot.</summary>
public sealed class GameInstance(Run run)
{
    /// <summary>The run's one music track, crossfaded between the menu and a level.</summary>
    public Music Music { get; } = new(run.Audio);
}

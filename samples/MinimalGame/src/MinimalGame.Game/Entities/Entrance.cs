using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.Entities;

/// <summary>A point a player arrives at from another room, named by the <see cref="Arrival"/> that leads here.</summary>
public sealed class Entrance : Entity
{
    /// <summary>What an exit in another room names to arrive here.</summary>
    /// <remarks>Place it clear of any exit, or the player leaves at once.</remarks>
    [Authorable(Required = true)]
    public string Key { get; private set; } = string.Empty;

    /// <param name="spawn">Where the player's top-left corner lands, placed as the player itself is.</param>
    public Entrance(EntitySpawn spawn)
        : base(spawn)
    {
    }
}

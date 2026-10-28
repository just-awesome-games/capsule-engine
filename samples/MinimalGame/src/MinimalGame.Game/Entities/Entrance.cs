using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.Entities;

/// <summary>A point a player arrives at from another room, the one an <see cref="Arrival"/> from that room finds.</summary>
public sealed class Entrance : Entity
{
    /// <summary>The room whose exit leads here. A room has one entrance from each room that leads to it.</summary>
    /// <remarks>Place it clear of any exit, or the player leaves at once.</remarks>
    [Authorable(Required = true)]
    public SceneKey From { get; private set; }

    /// <param name="spawn">Where the player's top-left corner lands, placed as the player itself is.</param>
    public Entrance(EntitySpawn spawn)
        : base(spawn)
    {
    }
}

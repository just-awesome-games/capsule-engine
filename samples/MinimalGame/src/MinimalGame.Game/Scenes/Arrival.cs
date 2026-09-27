using MinimalGame.Game.Entities;

namespace MinimalGame.Game.Scenes;

/// <summary>How the player comes into a room: at the <see cref="Entrance"/> whose <see cref="Entrance.Key"/> this names.</summary>
public readonly record struct Arrival(string Entrance);

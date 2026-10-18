using MinimalGame.Game.Entities;

namespace MinimalGame.Game.Scenes;

/// <summary>How the player comes into a room: at the <see cref="Entrance"/> whose <see cref="Entrance.Key"/> this names.</summary>
public record Arrival(string Entrance) : IScenePayload;

/// <summary>Arrives at the entrance keyed <c>west</c>, for <c>--payload WestArrival</c>.</summary>
public sealed record WestArrival() : Arrival("west");

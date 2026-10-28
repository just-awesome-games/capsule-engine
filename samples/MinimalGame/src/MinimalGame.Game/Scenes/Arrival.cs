using MinimalGame.Game.Entities;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// How the player comes into a room: at the <see cref="Entrance"/> from the room <paramref name="From"/> names, the
/// <see cref="Scene.DocumentKey"/> of the room left.
/// </summary>
public record Arrival(SceneKey? From) : IScenePayload;

/// <summary>Arrives at the hall's west entrance, as from the room, for <c>--payload WestArrival</c>.</summary>
public sealed record WestArrival() : Arrival(CapsuleAssets.Scenes.RoomScene);

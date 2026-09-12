using Capsule.Scenes;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The playable room: a scene that is a document and a class at once. <c>[SceneDocument("room")]</c>
/// names <c>Assets/Scenes/room.scene.json</c> and the <see cref="SceneContent"/> constructor is the
/// claim — a scene with one is composed from its document, entry by entry in file order.
/// <c>halls/hall.scene.json</c> is the contrasting case — a document claimed by no class at all, which
/// still loads and plays as a plain <see cref="Scene"/>. Everything that makes this room playable is
/// <see cref="PlayableScene"/>'s; the claim is all this class is.
/// </summary>
[SceneDocument("room")]
public sealed class Room(SceneContent content) : PlayableScene(content);

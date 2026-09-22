using Capsule.Rendering;
using Capsule.Scenes;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The playable room, composed from <c>Assets/Scenes/room.scene.json</c>, which
/// <c>[SceneDocument("room")]</c> names. Everything that makes it playable is
/// <see cref="PlayableScene"/>'s, so the claim is all this class is. <c>halls/hall.scene.json</c> is the
/// contrasting case, a document no class claims.
/// </summary>
[SceneDocument("room")]
public sealed class Room : PlayableScene
{
    public Room(SceneContent content)
        : base(content) =>
        Ambient = new ColorRgba(72, 76, 104); // The room is dusk.
}

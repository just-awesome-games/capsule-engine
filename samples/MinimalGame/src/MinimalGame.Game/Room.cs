using Capsule;
using Capsule.Scenes;

namespace MinimalGame.Game;

/// <summary>
/// The playable room: a scene that is a document and a class at once. The document is
/// <c>src/asset-sources/scenes/room.tmj</c>, which the build derives to
/// <c>assets/scenes/room.scene.json</c> beside the executable; <c>[SceneDocument("room")]</c> names
/// it, and without the attribute the kebab-cased class name would be the document name anyway. The
/// <see cref="SceneContent"/> constructor is the claim — a scene with one is composed from its
/// document, entry by entry in file order: the tile map first, then the <c>player</c> and
/// <c>sensor</c> placements.
/// <para>
/// This class is the code half of that scene: what the camera does, and what quitting means.
/// <c>hall.scene.json</c> is the contrasting case — a document claimed by no class at all, which
/// still loads and plays as a plain <see cref="Scene"/>.
/// </para>
/// </summary>
[SceneDocument("room")]
public sealed class Room : Scene
{
    public Room(SceneContent content)
        : base(content)
    {
    }

    /// <inheritdoc/>
    protected override void OnStart() => Camera.Teleport(FindSingle<Player>().Position);

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }

    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context) => Camera.Center = FindSingle<Player>().Position;
}

using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.Entities;

/// <summary>
/// Leaves for another room when the player walks into its doorway, arriving at the entrance there from this room.
/// </summary>
/// <param name="size">The doorway, from its entity's corner.</param>
public sealed class SceneExit(Vector2 size) : Component
{
    private readonly BoxCollider2D _doorway = new(size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
    private bool _leaving;

    /// <summary>The room to leave for.</summary>
    [Authorable(Required = true)]
    public SceneKey Destination { get; set; }

    protected override void OnAttached(Entity entity)
    {
        _doorway.ContactEntered += Enter;
        entity.Add(_doorway);
    }

    // The destination loads behind the room, collected with the arrival the doorway will carry. A room with a
    // second exit would prefetch only the last to start.
    protected override void OnStart() => Run.PrefetchScene(Destination, ArrivalHere());

    protected override void OnDetached(Entity entity)
    {
        _doorway.ContactEntered -= Enter;
        entity.Remove(_doorway);
    }

    // Requests the destination room once, however often the doorway is entered.
    private void Enter(ColliderContact2D contact)
    {
        if (!_leaving)
        {
            _leaving = true;
            Run.RequestScene(Destination, ArrivalHere());
        }
    }

    private Arrival ArrivalHere() => new(Entity!.Scene.DocumentKey);
}

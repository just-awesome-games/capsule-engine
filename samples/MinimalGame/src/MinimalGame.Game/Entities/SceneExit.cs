using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.Entities;

/// <summary>
/// Leaves for another room when the player's probe walks into its doorway, arriving at the entrance there that
/// its arrival key names.
/// </summary>
/// <param name="size">The doorway, from its entity's corner.</param>
public sealed class SceneExit(Vector2 size) : Component
{
    private readonly BoxCollider2D _doorway = new(size) { ReportsContacts = true, Detects = new(CollisionLayers.Probe) };
    private bool _leaving;

    /// <summary>The room to leave for.</summary>
    [Authorable(Required = true)]
    public SceneKey Destination { get; set; }

    /// <summary>The key of the <see cref="Entrance"/> in <see cref="Destination"/> the player arrives at.</summary>
    [Authorable(Required = true)]
    public string ArriveAt { get; set; } = string.Empty;

    protected override void OnAttached(Entity entity)
    {
        _doorway.ContactEntered += Enter;
        entity.Add(_doorway);
    }

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
            Run.RequestScene(Destination, new Arrival(ArriveAt));
        }
    }
}

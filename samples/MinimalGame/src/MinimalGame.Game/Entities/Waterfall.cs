using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The water pouring over a room's falls tiles, which bears the player down at its own gravity while the
/// player is inside it.
/// </summary>
public sealed class Waterfall : Entity
{
    /// <summary>The area's width and height in world units.</summary>
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    /// <summary>The gravity the player falls by inside, in world units per second squared.</summary>
    [Authorable(Required = true)]
    public float Gravity { get; private set; }

    /// <param name="spawn">The area's top-left corner.</param>
    public Waterfall(EntitySpawn spawn)
        : base(spawn)
    {
        BoxCollider2D area = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        area.ContactEntered += OnPlayerEntered;
        area.ContactExited += OnPlayerExited;
        Add(area);
    }

    private void OnPlayerEntered(ColliderContact2D contact) => ((Player)contact.OtherEntity!).GravityOverride = Gravity;

    private void OnPlayerExited(ColliderContact2D contact) => ((Player)contact.OtherEntity!).GravityOverride = null;
}

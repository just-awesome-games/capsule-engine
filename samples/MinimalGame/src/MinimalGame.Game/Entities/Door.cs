using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>A doorway the player walks into to leave for another room.</summary>
public sealed class Door : Entity
{
    private static readonly Vector2 Size = new(16f, 32f);

    /// <summary>The room this door leads to.</summary>
    [Authorable(Required = true)]
    public SceneKey Destination { get; private set; }

    /// <summary>The key of the <see cref="Entrance"/> in <see cref="Destination"/> the player arrives at.</summary>
    [Authorable(Required = true)]
    public string ArriveAt { get; private set; } = string.Empty;

    /// <param name="spawn">The doorway's top-left corner.</param>
    public Door(EntitySpawn spawn)
        : base(spawn)
    {
        SceneExit exit = new(Destination, ArriveAt);
        Add(exit);

        // Behind the player walking through it, and in front of the hills.
        ZIndex = -5;
        Add(new ColorRect(Size) { Color = ColorRgba.FromHex("#3a2a1c") });

        // Nothing blocks on the default layer the doorway sits on.
        BoxCollider2D doorway = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        doorway.ContactEntered += _ => exit.Leave();
        Add(doorway);
    }
}

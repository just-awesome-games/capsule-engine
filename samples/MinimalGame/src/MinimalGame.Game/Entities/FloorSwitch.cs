using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>A plate on the floor that starts the lift its placement names when the player steps on it.</summary>
public sealed class FloorSwitch : Entity
{
    private static readonly Vector2 Size = new(16f, 4f);

    private readonly ColorRect _plate;

    /// <summary>The lift this plate starts. room.scene.json names it by its entry id.</summary>
    [Authorable]
    public required Lift Lift { get; set; }

    /// <param name="spawn">The plate's top-left corner.</param>
    public FloorSwitch(EntitySpawn spawn)
        : base(spawn)
    {
        _plate = new ColorRect(Size) { Color = ColorRgba.FromHex("#a8433a") };
        Add(_plate);

        BoxCollider2D plate = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        plate.ContactEntered += OnPlayerEntered;
        Add(plate);
    }

    private void OnPlayerEntered(ColliderContact2D contact)
    {
        Lift.Running = true;
        _plate.Color = ColorRgba.FromHex("#5aa85a");
    }
}

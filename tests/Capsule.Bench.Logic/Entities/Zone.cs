using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A large sensor that reports every actor and wall cell it touches, as a water volume or a damage field would.</summary>
public sealed class Zone : Entity
{
    public int Occupants { get; private set; }

    public Zone(Vector2 position, Vector2 size)
        : base(position)
    {
        BoxCollider2D area = new(size) { ReportsContacts = true };
        area.Detects = new(CollisionLayers.Actor, CollisionLayers.Solid);
        area.ContactEntered += _ => Occupants++;
        area.ContactExited += _ => Occupants--;
        Add(area);
    }
}

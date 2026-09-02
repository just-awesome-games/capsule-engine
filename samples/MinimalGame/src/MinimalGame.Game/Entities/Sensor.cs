using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// An entity that collides without blocking. It sits on the <c>sensor</c> collision layer, which
/// <see cref="Player"/> detects but does not block on, so the player walks straight through it and
/// its contact is only reported. It listens to nothing itself: it never turns
/// <see cref="Collider2D.ReportsContacts"/> on, so it costs a shape in the world and no work.
/// </summary>
public sealed class Sensor : Entity
{
    private static readonly Vector2 Body = new(16f, 24f);

    public Sensor(EntitySpawn spawn)
        : base(spawn.Position)
    {
        Add(new QuadRenderer(Body, new ColorRgba(0x38, 0xA1, 0x69, 0x80)));
        Add(new BoxCollider2D(Body) { Layer = "sensor" });
    }
}

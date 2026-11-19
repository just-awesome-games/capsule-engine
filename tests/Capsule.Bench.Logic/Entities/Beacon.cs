namespace Capsule.Bench.Logic.Entities;

/// <summary>A placed sensor with the members a level's authored objects carry: an area, a glow colour, a period, a phase and a link to another.</summary>
public sealed class Beacon : Entity
{
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    [Authorable]
    public ColorRgba Glow { get; private set; } = ColorRgba.White;

    [Authorable]
    public int Period { get; private set; }

    [Authorable]
    public float Phase { get; private set; }

    [Authorable]
    public Beacon? Next { get; set; }

    public Beacon(EntitySpawn spawn)
        : base(spawn) =>
        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Sensor });
}

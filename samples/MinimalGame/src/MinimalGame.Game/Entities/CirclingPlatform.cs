using System.Numerics;
using Capsule;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// A slab that circles its spawn point at a constant rate. A body moved by its layer rides it round.
/// </summary>
public sealed class CirclingPlatform : Entity
{
    private static readonly Vector2 Size = new(24f, 6f);
    private const float Radius = 24f;
    private const int TurnTicks = 240;

    private readonly Vector2 _centre;

    // The angle in steps of the turn, wrapped each turn: a float angle that grows forever loses
    // precision.
    private int _tick;

    public CirclingPlatform(EntitySpawn spawn)
        : base(spawn)
    {
        _centre = Position;
        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Platform });
        Add(new ColorRect(Size) { Color = ColorRgba.FromHex("#88627a") });
        Teleport(Place(_tick));
    }

    protected override void OnStep(in StepContext context)
    {
        _tick = (_tick + 1) % TurnTicks;
        Position = Place(_tick);
    }

    // The slab's top-left corner, with its centre on the circle.
    private Vector2 Place(int tick)
    {
        float angle = MathF.Tau * tick / TurnTicks;

        return _centre + (Radius * new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle))) - (Size / 2f);
    }
}

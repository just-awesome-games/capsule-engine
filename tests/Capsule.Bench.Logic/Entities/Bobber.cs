using System.Numerics;
using Capsule;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A crate that bobs up and down about its home, carrying a sensor box a little larger than itself.</summary>
public sealed class Bobber : Entity
{
    private const float Reach = 6f;

    private const int PeriodSteps = 90;

    private readonly Vector2 _home;
    private int _tick;

    public Bobber(Vector2 home, int phase)
        : base(home)
    {
        _home = home;
        _tick = phase % PeriodSteps;
        Position = Bobbed();
        Add(new BoxCollider2D(new Vector2(24f, 10f)) { Layer = CollisionLayers.Crate });
        Add(new BoxCollider2D(new Vector2(26f, 12f)) { Layer = CollisionLayers.Sensor });
    }

    protected override void OnStep(in StepContext context)
    {
        _tick = (_tick + 1) % PeriodSteps;
        Position = Bobbed();
    }

    private Vector2 Bobbed() => _home + new Vector2(0f, Reach * DeterministicMath.Sin(MathF.Tau * _tick / PeriodSteps));
}

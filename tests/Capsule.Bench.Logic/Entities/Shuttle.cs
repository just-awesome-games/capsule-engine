using System.Numerics;
using Capsule;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A one-way slab that swings back and forth about its home, up and down or side to side, and carries whatever stands on it.</summary>
/// <remarks>It starts at its phase's point of the swing and holds still for its first step. Bodies placed on it land before it moves.</remarks>
public sealed class Shuttle : Entity
{
    public static readonly Vector2 Size = new(64f, 6f);

    private const float Reach = 24f;

    private const int PeriodSteps = 240;

    private readonly Vector2 _home;
    private readonly Vector2 _axis;
    private int _tick;
    private bool _moving;

    public Shuttle(Vector2 home, bool vertical, int phase)
        : base(home)
    {
        _home = home;
        _axis = vertical ? Vector2.UnitY : Vector2.UnitX;
        _tick = phase % PeriodSteps;
        Position = Swung();
        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Platform, OneWay = true });
    }

    protected override void OnStep(in StepContext context)
    {
        if (!_moving)
        {
            _moving = true;
            return;
        }

        _tick = (_tick + 1) % PeriodSteps;
        Position = Swung();
    }

    private Vector2 Swung() => _home + (Reach * DeterministicMath.Sin(MathF.Tau * _tick / PeriodSteps) * _axis);
}

using System.Numerics;
using Capsule;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A root that sways side to side over 63 links nested one below the next, with a collider on the last link.</summary>
/// <remarks>Every move of the root reaches the collider at the bottom of the chain.</remarks>
public sealed class Chain : Entity
{
    public const int Depth = 64;

    private const float Sway = 12f;

    private const int PeriodSteps = 120;

    private readonly Vector2 _home;
    private int _tick;

    public Chain(Vector2 position, int phase)
        : base(position)
    {
        _home = position;
        _tick = phase % PeriodSteps;

        Entity parent = this;
        for (int level = 1; level < Depth; level++)
        {
            parent = new Link(parent);
        }

        parent.Add(new BoxCollider2D(new Vector2(4f, 4f)) { Layer = CollisionLayers.Actor });
    }

    protected override void OnStep(in StepContext context)
    {
        _tick = (_tick + 1) % PeriodSteps;
        Position = _home + new Vector2(Sway * DeterministicMath.Sin(MathF.Tau * _tick / PeriodSteps), 0f);
    }

    private sealed class Link : Entity
    {
        internal Link(Entity parent)
            : base(parent, new Vector2(0f, 0.5f))
        {
        }
    }
}

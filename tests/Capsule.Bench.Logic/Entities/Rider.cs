using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A grounded body that paces a few pixels back and forth under gravity and is moved by platforms.</summary>
public sealed class Rider : Entity
{
    private const int PaceSteps = 32;

    private const float PaceSpeed = 0.25f;

    private readonly KinematicBody2D _body;
    private readonly int _phase;

    public Rider(Vector2 position, int index)
        : base(position)
    {
        _phase = index % (2 * PaceSteps);

        BoxCollider2D collider = new(new Vector2(6f, 12f)) { Layer = CollisionLayers.Actor };
        Add(collider);

        _body = new KinematicBody2D(collider) { Mode = BodyMode.Grounded };
        _body.BlocksOn(CollisionLayers.Solid, CollisionLayers.Platform);
        _body.MovedBy(CollisionLayers.Platform);
        Add(_body);
    }

    protected override void OnStep(in StepContext context)
    {
        float direction = ((context.Tick + _phase) / PaceSteps) % 2 == 0 ? 1f : -1f;
        _body.Move(new Vector2(direction * PaceSpeed, 4f));
    }
}

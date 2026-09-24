using System.Numerics;
using Capsule.Animation;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A kinematic body that walks until blocked and turns, animating as it goes; it collides with the room and never with another walker.</summary>
public sealed class Walker : Entity
{
    private readonly KinematicBody2D _body;
    private readonly SpriteAnimator _animator;
    private float _direction;

    public Walker(Vector2 position, int index)
        : base(position)
    {
        _direction = index % 2 == 0 ? 1f : -1f;

        BoxCollider2D collider = new(new Vector2(12f, 24f));
        collider.Layer = CollisionLayers.Actor;
        collider.SetFilter(CollisionLayers.Solid, CollisionLayers.Platform);
        Add(collider);

        _body = new KinematicBody2D(collider) { Mode = BodyMode.Grounded };
        _body.BlocksOn(CollisionLayers.Solid, CollisionLayers.Platform);
        Add(_body);

        SpriteRenderer renderer = new(CapsuleAssets.Sprites.Walker.Frames.Walk0);
        Add(renderer);

        _animator = new SpriteAnimator(renderer);
        Add(_animator);
    }

    protected override void OnStart() => _animator.Play(CapsuleAssets.Sprites.Walker.Clips.Walk);

    protected override void OnStep(in StepContext context)
    {
        _body.Move(new Vector2(_direction * 2f, 4f));
        if (_body.IsOnWall)
        {
            _direction = -_direction;
        }
    }
}

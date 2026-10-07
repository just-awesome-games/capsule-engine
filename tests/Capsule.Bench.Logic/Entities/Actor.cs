using System.Numerics;
using Capsule.Animation;
using Capsule.Bench.Logic.Components;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>A game's typical actor at six components: sprite, animator, hurtbox, on-screen notifier, a regenerating stat and a blinker.</summary>
/// <remarks>It drifts every step and turns at the viewport's edges.</remarks>
public sealed class Actor : Entity
{
    private static readonly Vector2 Size = new(12f, 24f);

    private readonly SpriteAnimator _animator;
    private Vector2 _velocity;

    public Actor(Vector2 position, Vector2 velocity, int index)
        : base(position)
    {
        _velocity = velocity;

        SpriteRenderer renderer = new(CapsuleAssets.Sprites.WalkerSheet.Frames.Walk0);
        Add(renderer);

        _animator = new SpriteAnimator(renderer);
        Add(_animator);

        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Actor });
        Add(new VisibleOnScreenNotifier2D { Rect = new Rect(Vector2.Zero, Size) });
        Add(new Regen(100) { Value = index % 100 });
        Add(new Blinker(renderer, 8 + (index % 5)));
    }

    protected override void OnStart() => _animator.Play(CapsuleAssets.Sprites.WalkerSheet.Clips.Walk);

    protected override void OnStep(in StepContext context)
    {
        Vector2 extent = World.ViewportSize - Size;
        Vector2 next = Position + _velocity;
        if (next.X < 0f || next.X > extent.X)
        {
            _velocity.X = -_velocity.X;
        }

        if (next.Y < 0f || next.Y > extent.Y)
        {
            _velocity.Y = -_velocity.Y;
        }

        Position = Vector2.Clamp(next, Vector2.Zero, extent);
    }
}

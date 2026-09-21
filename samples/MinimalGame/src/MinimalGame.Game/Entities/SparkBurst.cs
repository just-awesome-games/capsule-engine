using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The effect shape Unity and Godot use: an effect that outlives what asked for it is its own entity,
/// removed once its last particle dies.
/// </summary>
public sealed class SparkBurst : Entity
{
    private readonly ParticleEmitter _emitter;

    /// <param name="position">Where the burst starts, in world units.</param>
    public SparkBurst(Vector2 position)
        : base(position)
    {
        _emitter = new ParticleEmitter(Sprite.White, capacity: 8)
        {
            Lifetime = (0.15f, 0.35f),
            Speed = (60f, 140f),
            Spread = 360f,
            Gravity = new Vector2(0f, 300f),
            Scale = (1f, 2f),
            ScaleOverLifetime = Curve.Linear(1f, 0f),
            Color = Gradient.Linear(BoltTuning.Default.Tint, BoltTuning.Default.Tint with { A = 0 }),
            Blend = BlendMode.Additive,
        };
        Add(_emitter);
        _emitter.Emit(6);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (_emitter.Alive == 0)
        {
            Scene.Remove(this);
        }
    }
}

namespace MinimalGame.Game.Entities;

/// <summary>
/// A spark burst lives on its own entity so it outlives what asked for it. The entity leaves the
/// scene once its last particle dies and returns to its pool.
/// </summary>
public sealed class SparkBurst : Entity
{
    private readonly ParticleEmitter _emitter;

    public SparkBurst()
        : base(Vector2.Zero)
    {
        _emitter = new ParticleEmitter(Sprite.White, capacity: 8)
        {
            // A burst on a ring spreads evenly. Six sparks fly out six ways.
            Shape = EmitShape.Ring(1f),
            RadialSpeed = (60f, 140f),
            Lifetime = (0.15f, 0.35f),
            Gravity = new Vector2(0f, 300f),
            Scale = (1f, 2f),
            ScaleOverLifetime = Curve.Linear(1f, 0f),
            Color = Glow.FadeToClear(BoltTuning.Default.Tint),
            Blend = BlendMode.Additive,
            RemovesEntityWhenFinished = true,
        };
        Add(_emitter);
    }

    /// <summary>Places the burst and emits its particles.</summary>
    /// <param name="position">Where the burst starts, in world units.</param>
    /// <returns>This burst, for adding it to the scene in one expression.</returns>
    public SparkBurst Burst(Vector2 position)
    {
        Position = position;
        _emitter.Emit(6);

        return this;
    }
}

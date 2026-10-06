using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;

namespace MinimalGame.Game.Entities;

/// <summary>
/// What the <see cref="Player"/> fires: a tinted glow flying in one direction at a constant
/// speed, turned to face that way, until its lifetime is spent, then gone.
/// <see cref="Entity.Position"/> is its centre. It collides with nothing. Its levers live in
/// <see cref="BoltTuning"/>. Pooled by the player, so its per-life state is set in
/// <see cref="Fire"/> rather than the constructor.
/// </summary>
public sealed class Bolt : Entity
{
    private const int GlowTexels = 8;

    // The lamp's greyscale falloff pivoted at its centre, so the entity's scale sizes it about the
    // position rather than from a corner. It preloads through the pool the player forwards.
    private static readonly Sprite Glow = new(CapsuleAssets.Textures.GlowTexture, new TextureRegion(0, 0, GlowTexels, GlowTexels), new Vector2(GlowTexels / 2f));

    private readonly SpriteRenderer _sprite;

    private Vector2 _velocity;
    private Countdown _life;

    public Bolt()
        : base(Vector2.Zero)
    {
        _sprite = new SpriteRenderer(Glow) { Blend = BlendMode.Additive };
        Add(_sprite);
        Add(new PointLight { Radius = 5f, Color = ColorRgba.Yellow, Intensity = 0.75f });
    }

    /// <summary>Places and arms the bolt for one life: where it starts, which way it flies and how.</summary>
    /// <param name="position">Where the bolt starts: the muzzle, in world units.</param>
    /// <param name="direction">The unit vector the bolt travels along.</param>
    /// <param name="tuning">The levers the bolt flies on.</param>
    /// <returns>This bolt, so the caller can add it to the scene in one expression.</returns>
    public Bolt Fire(Vector2 position, Vector2 direction, in BoltTuning tuning)
    {
        Position = position;
        _velocity = direction * tuning.Speed;
        Rotation = DeterministicMath.Atan2(direction.Y, direction.X);
        _life.Start(tuning.LifetimeTicks);
        Scale = tuning.Size / GlowTexels;
        _sprite.Color = tuning.Tint;

        return this;
    }

    // Every live bolt can end at once, and each bursts into sparks from the scene's shared pool.
    /// <inheritdoc/>
    protected override void CollectAssets(AssetCollection assets) => assets.Pool<SparkBurst>(capacity: 8);

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        Position += _velocity * context.DeltaSeconds;

        _life.Step();
        if (!_life.IsRunning)
        {
            Scene.Add(Scene.Pool<SparkBurst>().Take().Burst(Position));
            Scene.Remove(this);
        }
    }
}

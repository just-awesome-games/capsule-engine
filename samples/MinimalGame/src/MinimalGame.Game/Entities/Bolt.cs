using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using MinimalGame.Game.Tiles;

namespace MinimalGame.Game.Entities;

/// <summary>
/// What the <see cref="Player"/> fires: a tinted glow flying in one direction at a constant
/// speed, turned to face that way, until it strikes solid terrain, breaking a brick, or its
/// lifetime is spent. <see cref="Entity.Position"/> is its centre. It has no collider: each step
/// casts a ray. Its levers live in
/// <see cref="BoltTuning"/>. Pooled by the player, so its per-life state is set in
/// <see cref="Fire"/> rather than the constructor.
/// </summary>
public sealed class Bolt : Entity
{
    private const int GlowTexels = 8;

    // The lamp's greyscale falloff pivoted at its centre, so the entity's scale sizes it about the
    // position rather than from a corner. It preloads through the pool the player forwards.
    private static readonly Sprite Glow = new(CapsuleAssets.Textures.GlowTexture, new TextureRegion(0, 0, GlowTexels, GlowTexels), new Vector2(GlowTexels / 2f));

    // Platforms let bolts through.
    private static readonly CollisionMask Strikes = new(CollisionLayers.Solid);

    private readonly SpriteRenderer _sprite;

    private Vector2 _direction;
    private float _speed;
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
        _direction = direction;
        _speed = tuning.Speed;
        Rotation = DeterministicMath.Atan2(direction.Y, direction.X);
        _life.Start(tuning.LifetimeTicks);
        Scale = tuning.Size / GlowTexels;
        _sprite.Color = tuning.Tint;

        return this;
    }

    // Each bolt bursts into sparks from the scene's shared pool once per life. Its life outlasts a
    // burst, so one burst per bolt is its worst case.
    /// <inheritdoc/>
    protected override void CollectAssets(AssetCollection assets) => assets.Pool<SparkBurst>(capacity: 1);

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        float reach = _speed * context.DeltaSeconds;
        if (Scene.Collision.Raycast(Position, _direction, reach, Strikes, out RayHit2D hit))
        {
            if (Scene.TileOf(hit.Target) is { Type: Brick } brick)
            {
                brick.Map.RemoveTile(brick.X, brick.Y);
            }

            Burst(hit.Point);
            return;
        }

        Position += _direction * reach;

        _life.Step();
        if (!_life.IsRunning)
        {
            Burst(Position);
        }
    }

    private void Burst(Vector2 at)
    {
        Scene.Add(Scene.Pool<SparkBurst>().Take().Burst(at));
        Scene.Remove(this);
    }
}

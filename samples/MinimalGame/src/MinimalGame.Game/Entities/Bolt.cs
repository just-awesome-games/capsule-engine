using MinimalGame.Game.Tiles;

namespace MinimalGame.Game.Entities;

/// <summary>
/// What the <see cref="Player"/> fires: a tinted glow centred on <see cref="Entity.Position"/> that flies
/// straight until it strikes solid terrain, breaking a brick, or its lifetime is spent. The player pools
/// it, and <see cref="Fire"/> sets its per-life state.
/// </summary>
public sealed class Bolt : Entity
{
    // What stops the player stops a bolt, except platforms, which let bolts through.
    private static readonly CollisionMask Strikes = CollisionLayers.Blocking.Without(CollisionLayers.Platform);

    private readonly SpriteRenderer _sprite;

    private Vector2 _direction;
    private float _speed;
    private Countdown _life;

    public Bolt()
        : base(Vector2.Zero)
    {
        // The glow preloads through the pool the player forwards.
        _sprite = Glow.Renderer(ColorRgba.White);
        Add(_sprite);
        Add(new PointLight { Radius = 5f, Color = ColorRgba.Yellow, Intensity = 0.75f });
    }

    /// <summary>Places and arms the bolt for one life: where it starts, which way it flies and how.</summary>
    /// <param name="position">Where the bolt starts: the muzzle, in world units.</param>
    /// <param name="direction">The unit vector the bolt travels along.</param>
    /// <param name="tuning">The levers the bolt flies on.</param>
    /// <returns>This bolt, for adding it to the scene in one expression.</returns>
    public Bolt Fire(Vector2 position, Vector2 direction, in BoltTuning tuning)
    {
        Position = position;
        _direction = direction;
        _speed = tuning.Speed;
        Rotation = DeterministicMath.Atan2(direction.Y, direction.X);
        _life.Start(tuning.LifetimeTicks);
        Scale = tuning.Size / Glow.Texels;
        _sprite.Color = tuning.Tint;

        return this;
    }

    // Each bolt bursts into sparks from the scene's shared pool once per life. Its life outlasts a
    // burst, which makes one burst per bolt its worst case.
    /// <inheritdoc/>
    protected override void CollectAssets(AssetCollection assets) => assets.Pool<SparkBurst>(capacity: 1);

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        // It has no collider. Each step casts a ray as far as the step carries it.
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

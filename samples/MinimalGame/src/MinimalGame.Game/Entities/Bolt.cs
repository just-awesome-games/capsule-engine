using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;

namespace MinimalGame.Game.Entities;

/// <summary>
/// What the <see cref="Player"/> fires: a flat tinted rect flying in one direction at a constant
/// speed until its lifetime is spent, then gone. <see cref="Entity.Position"/> is its centre. It
/// collides with nothing. Its levers live in <see cref="BoltTuning"/>. Pooled by the player, so its
/// per-life state is set in <see cref="Fire"/> rather than the constructor.
/// </summary>
public sealed class Bolt : Entity
{
    // The engine's white texel pivoted at its own centre, so the entity's scale sizes it about the
    // position rather than from a corner.
    private static readonly Sprite Centred = new(TextureHandle.White, new TextureRegion(0, 0, 1, 1), new Vector2(0.5f, 0.5f));

    private readonly SpriteRenderer _sprite;
    private readonly EntityPool<SparkBurst> _sparks;

    private Vector2 _velocity;
    private Countdown _life;

    /// <param name="sparks">The pool <see cref="SparkBurst"/> is taken from when this bolt's life ends.</param>
    public Bolt(EntityPool<SparkBurst> sparks)
        : base(Vector2.Zero)
    {
        _sparks = sparks;

        _sprite = new SpriteRenderer(Centred);
        Add(_sprite);
    }

    /// <summary>Places and arms the bolt for one life: where it starts, which way it flies and how.</summary>
    /// <param name="position">Where the bolt starts: the muzzle, in world units.</param>
    /// <param name="direction">The sign of the X the bolt travels along; negative is left.</param>
    /// <param name="tuning">The levers the bolt flies on.</param>
    /// <returns>This bolt, so the caller can add it to the scene in one expression.</returns>
    public Bolt Fire(Vector2 position, float direction, in BoltTuning tuning)
    {
        Position = position;
        _velocity = new Vector2(direction < 0f ? -tuning.Speed : tuning.Speed, 0f);
        _life.Start(tuning.LifetimeTicks);
        Scale = tuning.Size;
        _sprite.Color = tuning.Tint;

        return this;
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        Position += _velocity * context.DeltaSeconds;

        _life.Step();
        if (!_life.IsRunning)
        {
            Scene.Add(_sparks.Take().Burst(Position));
            Scene.Remove(this);
        }
    }
}

using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// An entity that collides without blocking. It sits on the <c>hazard</c> collision layer, which
/// <see cref="Player"/> detects but does not block on, so the player walks straight through it and
/// its contact is only reported. It listens to nothing itself: it never turns
/// <see cref="Collider2D.ReportsContacts"/> on, so it costs a shape in the world and no work. Its
/// sprite spins about its centre while its box stays axis-aligned: rotation is presentation only,
/// and the shape the player detects never turns.
/// </summary>
public sealed class Hazard : Entity
{
    private static readonly Vector2 Body = new(16f, 24f);

    /// <summary>How fast the sprite turns, in radians per second: one turn every two seconds.</summary>
    private const float SpinSpeed = MathF.PI;

    /// <summary>The whole of <c>textures/hazard.png</c>, pivoted at its centre so it spins in place.</summary>
    private static readonly Sprite Field = new(CapsuleAssets.Textures.Hazard, new TextureRegion(0, 0, 16, 24), Body / 2f);

    private readonly SpriteRenderer _sprite;

    public Hazard(EntitySpawn spawn)
        : base(spawn)
    {
        // Offset by the pivot, so the frame at rest covers the corner-anchored box exactly.
        _sprite = new SpriteRenderer(Field) { Offset = Body / 2f };
        Add(_sprite);
        Add(new BoxCollider2D(Body) { Layer = CollisionLayers.Hazard });
    }

    protected override void OnStep(in StepContext context)
    {
        // Wrapped each step: a float angle that grows forever loses precision.
        _sprite.Rotation = (_sprite.Rotation + (SpinSpeed * context.DeltaSeconds)) % MathF.Tau;
    }
}

using System.Numerics;
using Capsule.Animation;
using Capsule.Particles;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>A portal the player walks into to leave for another room.</summary>
public sealed class Door : Entity
{
    private static readonly Vector2 Size = new(16f, 32f);

    /// <summary>The whole of <c>textures/portal.png</c>, a stone arch round a violet void.</summary>
    private static readonly Sprite Arch = new(CapsuleAssets.Textures.PortalTexture, new TextureRegion(0, 0, 16, 32));

    /// <summary>The swirl's centre in the arch, where the motes are drawn in.</summary>
    private static readonly Vector2 PortalCentre = new(8f, 17f);

    private static readonly ColorRgba PortalGlow = ColorRgba.FromHex("#c8a0ff");

    /// <summary>Where the door leads.</summary>
    [Authorable]
    public SceneExit Exit { get; } = new();

    /// <param name="spawn">The doorway's top-left corner.</param>
    public Door(EntitySpawn spawn)
        : base(spawn)
    {
        Add(Exit);

        // Behind the player walking through it, and in front of the hills.
        ZIndex = -5;
        Add(new SpriteRenderer(Arch));

        // Motes drawn in to the portal's heart. Local, so they stay with the door wherever it stands.
        // Each is brightest on the rim and fades before it reaches the centre.
        Add(new ParticleEmitter(Sprite.White, capacity: 24)
        {
            Space = ParticleSpace.Local,
            Offset = PortalCentre,
            Shape = EmitShape.Ring(7f),
            RadialSpeed = (-20f, -16f),
            Rate = 40f,
            Lifetime = (0.3f, 0.3f),
            Scale = (2f, 2f),
            ScaleOverLifetime = Curve.Linear(1f, 0.5f),
            Color = Gradient.Linear(PortalGlow, PortalGlow with { A = 0 }),
            Blend = BlendMode.Additive,
        });

        BoxCollider2D doorway = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        doorway.ContactEntered += _ => Exit.Leave();
        Add(doorway);
    }
}

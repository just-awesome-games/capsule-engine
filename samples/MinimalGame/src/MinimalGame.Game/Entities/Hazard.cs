using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Particles;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// Collides without blocking: on the <c>hazard</c> layer the <see cref="Player"/> reports but
/// walks through, and reporting nothing itself. Its box stays axis-aligned while the nested
/// <see cref="Visual"/> child spins the sprite and the nested <see cref="Orbit"/> child carries
/// the spark round it.
/// </summary>
public sealed class Hazard : Entity
{
    private static readonly Vector2 Body = new(16f, 24f);

    /// <summary>The whole of <c>textures/hazard.png</c>, pivoted at its centre so it spins in place.</summary>
    private static readonly Sprite Field = new(CapsuleAssets.Textures.HazardTexture, new TextureRegion(0, 0, 16, 24), Body / 2f);

    public Hazard(EntitySpawn spawn)
        : base(spawn)
    {
        Add(new BoxCollider2D(Body) { Layer = CollisionLayers.Hazard });

        _ = new Visual(this, Body / 2f);
        _ = new Orbit(this, Body / 2f);
    }

    /// <summary>
    /// A child at <c>centre</c> that draws the frame and turns itself each step. A child rather
    /// than the hazard, which holds a collider and so cannot turn.
    /// </summary>
    private sealed class Visual : Entity
    {
        /// <summary>Radians per second: one turn every two seconds.</summary>
        private const float SpinSpeed = MathF.PI;

        internal Visual(Entity parent, Vector2 centre)
            : base(parent, centre) =>
            Add(new SpriteRenderer(Field));

        // Wrapped each step: a float angle that grows forever loses precision.
        protected override void OnStep(in StepContext context) =>
            Rotation = (Rotation + (SpinSpeed * context.DeltaSeconds)) % MathF.Tau;
    }

    /// <summary>
    /// A child at <c>centre</c> that turns itself each step, carrying the spark at the orbit
    /// radius. A child rather than the hazard, which holds a collider and so cannot turn.
    /// </summary>
    private sealed class Orbit : Entity
    {
        /// <summary>Radians per second: one orbit every three seconds, against the spin.</summary>
        private const float OrbitSpeed = -MathF.Tau / 3f;

        internal Orbit(Entity parent, Vector2 centre)
            : base(parent, centre) =>
            _ = new Spark(this);

        protected override void OnStep(in StepContext context) =>
            Rotation = (Rotation + (OrbitSpeed * context.DeltaSeconds)) % MathF.Tau;
    }

    // A world-space trail from a moving emitter, and the frame it trails behind, at the orbit radius.
    // A child of Orbit rather than the hazard, which holds a collider and so cannot turn.
    private sealed class Spark : Entity
    {
        /// <summary>World units from the centre: where <see cref="Orbit"/> carries <see cref="Spark"/>.</summary>
        private const float OrbitRadius = 20f;

        private static readonly Sprite SparkFrame = new(CapsuleAssets.Textures.HazardTexture, new TextureRegion(6, 10, 4, 4), new Vector2(2f, 2f));

        // The trail's colour: a warm orange fading to nothing.
        private static readonly ColorRgba SparkOrange = new(255, 150, 40);


        internal Spark(Entity parent)
            : base(parent, new Vector2(0f, -OrbitRadius))
        {
            // Added before the frame, so the trail draws under the spark and, at the room's shared
            // ZIndex, over the tiles.
            Add(new ParticleEmitter(SparkFrame, capacity: 48)
            {
                // Emits as the spark moves, half a particle per unit; Emitting is on by default.
                RateOverDistance = 0.5f,
                Lifetime = (0.3f, 0.5f),
                Speed = (0f, 10f),
                Spread = 360f,
                InheritVelocity = 0.2f,
                Scale = (1f, 1f),
                ScaleOverLifetime = Curve.Linear(1f, 0f),
                Color = Gradient.Linear(SparkOrange, SparkOrange with { A = 0 }),
                Blend = BlendMode.Additive,
            });
            Add(new SpriteRenderer(SparkFrame));
        }
    }
}

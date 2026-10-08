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
/// A box on the <c>hazard</c> layer, which the <see cref="Player"/> reports and walks through.
/// An entity holding a collider cannot turn, so the nested <see cref="Visual"/> child spins the
/// sprite and the nested <see cref="Orbit"/> child carries the spark round it. The first touch of
/// the player breaks the spark loose, and it flies off along its orbit's tangent. The box keeps hurting.
/// </summary>
public sealed class Hazard : Entity
{
    private static readonly Vector2 Body = new(16f, 24f);

    /// <summary>The whole of <c>textures/hazard.png</c>, pivoted at its centre so it spins in place.</summary>
    private static readonly Sprite Field = new(CapsuleAssets.Textures.HazardTexture, new TextureRegion(0, 0, 16, 24), Body / 2f);

    private readonly Spark _spark;

    public Hazard(EntitySpawn spawn)
        : base(spawn)
    {
        // Reports the player's body, so the touch is seen from this side too.
        BoxCollider2D box = new(Body)
        {
            Layer = CollisionLayers.Hazard,
            ReportsContacts = true,
            Detects = new(CollisionLayers.Player),
        };
        box.ContactEntered += OnTouched;
        Add(box);

        _ = new Visual(this, Body / 2f);
        _spark = new Orbit(this, Body / 2f).Spark;
    }

    private void OnTouched(ColliderContact2D contact)
    {
        if (_spark.Parent is not Orbit orbit)
        {
            return;
        }

        Vector2 heading = orbit.Tangent;

        // A root draws at its own band alone. The spark takes the hazard's.
        _spark.ZIndex = ZIndex;
        _spark.Parent = null;
        _spark.Fly(heading);
    }

    /// <summary>A child at <c>centre</c> that draws the frame and turns itself each step.</summary>
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

    /// <summary>A child at <c>centre</c> that turns itself each step, carrying the spark at the orbit radius.</summary>
    private sealed class Orbit : Entity
    {
        /// <summary>Radians per second: one orbit every three seconds, against the spin.</summary>
        private const float OrbitSpeed = -MathF.Tau / 3f;

        internal Spark Spark { get; }

        internal Orbit(Entity parent, Vector2 centre)
            : base(parent, centre) =>
            Spark = new Spark(this);

        /// <summary>The unit direction <see cref="Spark"/> is moving in now: its offset from the centre, turned a quarter the way the orbit turns.</summary>
        internal Vector2 Tangent
        {
            get
            {
                Vector2 offset = Spark.WorldPosition - WorldPosition;
                float turn = MathF.Sign(OrbitSpeed);
                return Vector2.Normalize(new Vector2(-turn * offset.Y, turn * offset.X));
            }
        }

        protected override void OnStep(in StepContext context) =>
            Rotation = (Rotation + (OrbitSpeed * context.DeltaSeconds)) % MathF.Tau;
    }

    // A world-space trail from a moving emitter, and the frame it trails behind, at the orbit radius.
    private sealed class Spark : Entity
    {
        /// <summary>World units per second once loose.</summary>
        private const float FlySpeed = 90f;

        /// <summary>World units from the centre: where <see cref="Orbit"/> carries <see cref="Spark"/>.</summary>
        private const float OrbitRadius = 20f;

        private static readonly Sprite SparkFrame = new(CapsuleAssets.Textures.HazardTexture, new TextureRegion(6, 10, 4, 4), new Vector2(2f, 2f));

        // The trail's colour: a warm orange fading to nothing.
        private static readonly ColorRgba SparkOrange = new(255, 150, 40);

        private Vector2 _velocity;

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

        internal void Fly(Vector2 heading)
        {
            _velocity = heading * FlySpeed;

            // A notifier refuses a turned ancestry. The spark drops the orbit's turn first.
            Rotation = 0f;
            VisibleOnScreenNotifier2D notifier = new() { Rect = new Rect(new Vector2(-2f, -2f), new Vector2(4f, 4f)) };
            notifier.ScreenExited += OnLeftView;
            Add(notifier);
        }

        protected override void OnStep(in StepContext context)
        {
            if (_velocity != Vector2.Zero)
            {
                Position += _velocity * context.DeltaSeconds;
            }
        }

        // Also raised as the spark leaves the scene, when it is no longer there to remove.
        private void OnLeftView()
        {
            if (SceneOrNull is { } scene && !IsRemovalPending)
            {
                scene.Remove(this);
            }
        }
    }
}

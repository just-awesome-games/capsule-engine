using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.Scenes.Animation;
using Capsule.Scenes.Audio;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The walking, falling, jumping body spawned by the <c>player</c> entries of
/// <c>scenes/room.scene.json</c> and <c>scenes/halls/hall.scene.json</c>.
/// <para>
/// A concrete entity with one public constructor taking an <see cref="EntitySpawn"/> claims the
/// key its namespace names as the document entry type it spawns from: this class sits directly under
/// <c>MinimalGame.Game.Entities</c>, so the <c>Entities</c> segment falls away and <c>Player</c> answers
/// to <c>"player"</c> with nothing registered by hand. One filed at <c>Entities/Enemies/Bat.cs</c> would
/// answer to <c>"enemies/bat"</c>; <c>[SpawnType("...")]</c> names a whole key in place of either.
/// </para>
/// <para>
/// <see cref="Entity.Position"/> is the top-left corner of the 8x8 body: both box colliders are
/// corner-anchored, and the sprite anchors its frame's bottom-centre at that same pivot offset from
/// the corner, so the frame covers the body facing either way. Anchoring an authored coordinate is
/// each entity's own convention.
/// </para>
/// <para>
/// There are two colliders and two independent filters. The body is the box
/// <see cref="KinematicBody2D"/> sweeps, and <see cref="KinematicBody2D.BlocksOn"/> names what stops
/// that sweep — <c>solid</c> and <c>platform</c>, the layers the room's tiles are authored on — while
/// it reports nothing. The inset hurtbox blocks nothing and is the only one reporting contacts, and
/// its <see cref="Collider2D.SetFilter"/> names what it reports: <c>sensor</c> alone, so the player
/// walks through a <see cref="Sensor"/>, says so, and spends a point of <see cref="Health"/> on it.
/// </para>
/// <para>
/// The squash-and-stretch is presentation and nothing more. Jumping and landing each throw the
/// sprite's <see cref="SpriteRenderer.Scale"/> off <see cref="Vector2.One"/> and it eases back;
/// both colliders keep their boxes throughout, so a stretched player is no taller to the physics
/// than a resting one.
/// </para>
/// </summary>
public sealed class Player : Entity
{
    /// <summary>Sensor contacts the player survives; the health it starts a room with.</summary>
    public const int MaxHealth = 4;

    /// <summary>World units per second.</summary>
    private const float WalkSpeed = 80f;

    /// <summary>World units per second squared, downwards in a Y-down world.</summary>
    private const float Gravity = 600f;

    /// <summary>World units per second at take-off; an apex of about 40px, clearing a two-tile ledge.</summary>
    private const float JumpSpeed = 220f;

    /// <summary>The body's edge in world units, and the frame's in texels: one texel per unit.</summary>
    private const int BodyPixels = 8;

    /// <summary>
    /// The scale the sprite snaps to on take-off: tall and thin, the classic platformer stretch.
    /// Tune the pair together — the product is what reads as volume, and nothing enforces it.
    /// </summary>
    private static readonly Vector2 JumpStretch = new(0.6f, 1.4f);

    /// <summary>The scale the sprite snaps to on landing: wide and flat, the stretch inverted.</summary>
    private static readonly Vector2 LandSquash = new(1.4f, 0.6f);

    /// <summary>
    /// Scale units per second each axis walks back towards 1 after an impulse. At 1.6 the 0.4 of
    /// either impulse is spent in a quarter second; raise it for a snappier recovery, lower it to
    /// let the deformation linger.
    /// </summary>
    private const float ScaleRecovery = 1.6f;

    /// <summary>
    /// How far the hurtbox is drawn in from each of the body's edges, in world units. A hurtbox
    /// smaller than the drawn frame is the grace 2D games give the player: a near miss reads as a
    /// miss. Widen it towards zero to make hits generous, inset it further to make them forgiving.
    /// </summary>
    private const int HurtboxInset = 1;

    private static readonly Vector2 Body = new(BodyPixels, BodyPixels);

    private static readonly Vector2 Hurtbox =
        new(BodyPixels - (HurtboxInset * 2), BodyPixels - (HurtboxInset * 2));

    /// <summary>
    /// The frame's pivot, in texels from its top-left corner, and the same vector from the body's
    /// corner to the point it anchors. Authored bottom-centre in every frame of the sheet on purpose:
    /// a flip and a scale both work about the pivot, so the horizontal centre keeps the drawn frame
    /// over the corner-anchored body in both facings, and the bottom edge keeps a squashed or
    /// stretched frame standing on the floor the body stands on.
    /// </summary>
    private static readonly Vector2 Pivot = CapsuleAssets.Sprites.Actors.Player.Frames.Idle0.Pivot;

    private readonly SpriteRenderer _sprite;
    private readonly SpriteAnimator _animator;
    private readonly KinematicBody2D _body;
    private readonly BoxCollider2D _hurtbox;
    private readonly AudioSource _footfall;

    private Vector2 _velocity;

    public Player(EntitySpawn spawn)
        : base(spawn.Position)
    {
        _sprite = new SpriteRenderer(CapsuleAssets.Sprites.Actors.Player.Frames.Idle0) { Offset = Pivot };
        Add(_sprite);

        // Named rather than found: an entity drawing itself as several sprites animates the one
        // it says. Its frames advance on ticks, so the frame the player is on is simulation state
        // like its position.
        _animator = new SpriteAnimator(_sprite);
        Add(_animator);

        BoxCollider2D bodyCollider = new(Body);
        Add(bodyCollider);

        _body = new KinematicBody2D(bodyCollider);
        _body.BlocksOn("solid", "platform");
        Add(_body);

        _hurtbox = new BoxCollider2D(Hurtbox)
        {
            Offset = new Vector2(HurtboxInset, HurtboxInset),
            ReportsContacts = true,
        };
        _hurtbox.SetFilter("sensor");
        _hurtbox.ContactEntered += OnHurtboxEntered;
        _hurtbox.ContactExited += OnHurtboxExited;
        Add(_hurtbox);

        _footfall = new AudioSource(CapsuleAssets.Audio.StepSoft);
        Add(_footfall);
    }

    /// <summary>
    /// What is left of <see cref="MaxHealth"/>: one spent on every sensor contact entered, and never
    /// below zero. Simulation state like a position, so the interface reads it on the step it changed.
    /// </summary>
    public int Health { get; private set; } = MaxHealth;

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        float delta = context.DeltaSeconds;

        // Recovery runs before this step's impulses, so an impulse set below is drawn whole.
        // On the fixed step, so the deformation is identical on every machine and frame rate.
        _sprite.Scale = new Vector2(
            Approach(_sprite.Scale.X, 1f, ScaleRecovery * delta),
            Approach(_sprite.Scale.Y, 1f, ScaleRecovery * delta));

        // The body applies no forces: velocity is the game's, every step.
        _velocity.X = context.Input.Axis(GameInput.Move) * WalkSpeed;
        _velocity.Y += Gravity * delta;

        // Facing is kept through a standstill, so the player stops looking where it walked.
        if (_velocity.X != 0f)
        {
            _sprite.FlipX = _velocity.X < 0f;
        }

        // Asked every step: the animator ignores the clip already playing, so the cycle runs
        // instead of restarting on frame 0.
        _animator.Play(_velocity.X != 0f ? CapsuleAssets.Sprites.Actors.Player.Clips.Walk : CapsuleAssets.Sprites.Actors.Player.Clips.Idle);

        // IsOnFloor is state as of the last Move, so this reads the previous step's landing.
        bool wasOnFloor = _body.IsOnFloor;
        if (wasOnFloor && context.Input.WasPressed(GameInput.Jump))
        {
            _velocity.Y = -JumpSpeed;

            // Neither collider follows the frame, so this taller player is exactly as tall to the
            // sweep below as a resting one.
            _sprite.Scale = JumpStretch;
            Log.Info("jumped");
        }

        _body.Move(_velocity * delta);

        if (_body.IsOnFloor)
        {
            _velocity.Y = 0f;

            if (!wasOnFloor)
            {
                // The room's ledges collide on their top face alone, so landing on one names
                // 'platform' here while a jump up through it names nothing at all.
                foreach (ColliderContact2D contact in _body.MoveContacts)
                {
                    if (contact.Normal.Y < 0f)
                    {
                        _sprite.Scale = LandSquash;
                        _footfall.Play();
                        Log.Info("landed on " + contact.LayerName);
                        break;
                    }
                }
            }
        }

        if (_body.IsOnCeiling)
        {
            _velocity.Y = 0f;
        }
    }

    // Named methods rather than lambdas: a handler with a name is one a subclass or a reader can
    // find, and it can be detached by the same method group that subscribed it.
    private void OnHurtboxEntered(ColliderContact2D contact)
    {
        Health = Math.Max(Health - 1, 0);
        Log.Info(FormattableString.Invariant($"entered {contact.LayerName} at {contact.Point}, health {Health}"));
    }

    private void OnHurtboxExited(ColliderContact2D contact) =>
        Log.Info(FormattableString.Invariant($"exited {contact.LayerName} at {contact.Point}"));

    /// <summary>
    /// Moves <paramref name="value"/> towards <paramref name="target"/> by at most
    /// <paramref name="maxDelta"/>, landing exactly on it rather than overshooting.
    /// </summary>
    private static float Approach(float value, float target, float maxDelta) =>
        value > target ? MathF.Max(value - maxDelta, target) : MathF.Min(value + maxDelta, target);
}

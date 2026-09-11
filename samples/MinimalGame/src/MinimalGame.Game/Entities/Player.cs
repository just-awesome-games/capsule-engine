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
/// its <see cref="Collider2D.SetFilter"/> names what it reports: <c>hazard</c> alone, so the player
/// walks through a <see cref="Hazard"/>, says so, and spends a point of <see cref="Health"/> on it.
/// </para>
/// <para>
/// The squash-and-stretch is presentation and nothing more. Jumping and landing each throw the
/// sprite's <see cref="SpriteRenderer.Scale"/> off <see cref="Vector2.One"/> and it eases back;
/// both colliders keep their boxes throughout, so a stretched player is no taller to the physics
/// than a resting one.
/// </para>
/// <para>
/// Its designer-owned levers live in <see cref="PlayerTuning"/>, the way a Unity ScriptableObject or
/// a Godot Resource would hold them; nothing in the engine knows that record exists.
/// </para>
/// </summary>
public sealed class Player : Entity
{
    /// <summary>The levers this player runs on, fixed for its lifetime.</summary>
    public ref readonly PlayerTuning Tuning => ref _tuning;

    /// <summary>
    /// What is left of <see cref="PlayerTuning.MaxHealth"/>: one spent on every hazard contact
    /// entered, and never below zero. Simulation state like a position, so the interface reads it on
    /// the step it changed.
    /// </summary>
    public int Health { get; private set; }

    /// <summary>The body's edge in world units, and the frame's in texels: one texel per unit.</summary>
    private const int BodyPixels = 8;

    private static readonly Vector2 Body = new(BodyPixels, BodyPixels);

    /// <summary>
    /// The frame's pivot, in texels from its top-left corner, and the same vector from the body's
    /// corner to the point it anchors. Authored bottom-centre in every frame of the sheet on purpose:
    /// a flip and a scale both work about the pivot, so the horizontal centre keeps the drawn frame
    /// over the corner-anchored body in both facings, and the bottom edge keeps a squashed or
    /// stretched frame standing on the floor the body stands on.
    /// </summary>
    private static readonly Vector2 Pivot = CapsuleAssets.Sprites.Actors.Player.Frames.Idle0.Pivot;

    // Entity-specific components
    private readonly SpriteRenderer _sprite;
    private readonly SpriteAnimator _animator;
    private readonly KinematicBody2D _body;
    private readonly BoxCollider2D _hurtbox;
    private readonly AudioSource _footfall;
    private readonly PlayerTuning _tuning = PlayerTuning.Default;

    private Vector2 _velocity;

    public Player(EntitySpawn spawn)
        : base(spawn.Position)
    {
        Health = _tuning.MaxHealth;

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
        _body.BlocksOn(CollisionLayers.Blocking);
        Add(_body);

        float hurtboxEdge = BodyPixels - (_tuning.HurtboxInset * 2);
        _hurtbox = new BoxCollider2D(new Vector2(hurtboxEdge, hurtboxEdge))
        {
            Offset = new Vector2(_tuning.HurtboxInset, _tuning.HurtboxInset),
            ReportsContacts = true,
        };
        _hurtbox.SetFilter(CollisionLayers.Damaging);
        _hurtbox.ContactEntered += OnHurtboxEntered;
        _hurtbox.ContactExited += OnHurtboxExited;
        Add(_hurtbox);

        _footfall = new AudioSource(CapsuleAssets.Audio.StepSoft) { Bus = AudioBuses.Sfx };
        Add(_footfall);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        float delta = context.DeltaSeconds;

        // Recovery runs before this step's impulses, so an impulse set below is drawn whole.
        // On the fixed step, so the deformation is identical on every machine and frame rate.
        _sprite.Scale = new Vector2(
            Approach(_sprite.Scale.X, 1f, _tuning.ScaleRecovery * delta),
            Approach(_sprite.Scale.Y, 1f, _tuning.ScaleRecovery * delta));

        // The body applies no forces: velocity is the game's, every step.
        _velocity.X = context.Input.Axis(GameInput.Move) * _tuning.WalkSpeed;
        _velocity.Y += _tuning.Gravity * delta;

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
            _velocity.Y = -_tuning.JumpSpeed;

            // Neither collider follows the frame, so this taller player is exactly as tall to the
            // sweep below as a resting one.
            _sprite.Scale = _tuning.JumpStretch;
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
                        _sprite.Scale = _tuning.LandSquash;
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

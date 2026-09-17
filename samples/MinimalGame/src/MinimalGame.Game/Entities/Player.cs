using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Assets.Generated;
using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The walking, falling, jumping body the <c>player</c> document entries spawn.
/// <see cref="Entity.Position"/> is the top-left corner of the 8x8 body. The body collider is what
/// the <see cref="KinematicBody2D"/> sweeps and blocks on; the inset hurtbox blocks nothing and
/// reports <c>hazard</c> contacts alone. Everything visual hangs on the nested <see cref="Visual"/>
/// child, which reads the facts this root publishes; a shot leaves from the muzzle socket of the
/// frame that child draws, so the root fires in its late step, once the frame has settled. Its
/// levers live in <see cref="PlayerTuning"/>, the bolt's in <see cref="BoltTuning"/>.
/// </summary>
public sealed class Player : Entity
{
    /// <summary>The levers this player runs on, fixed for its lifetime.</summary>
    public ref readonly PlayerTuning Tuning => ref _tuning;

    /// <summary>What is left of <see cref="PlayerTuning.MaxHealth"/>: one spent per hazard contact, never below zero.</summary>
    public int Health { get; private set; }

    /// <summary>The velocity the body moves at, in world units per second, as of the last step.</summary>
    public Vector2 Velocity => _velocity;

    /// <summary>Whether the last step took off from the floor; cleared as each step begins.</summary>
    public bool JumpedThisStep { get; private set; }

    /// <summary>Whether the last step landed on a floor; cleared as each step begins.</summary>
    public bool LandedThisStep { get; private set; }

    /// <summary>Whether the last step fired a bolt; cleared as each step begins.</summary>
    public bool ShotThisStep { get; private set; }

    /// <summary>
    /// The muzzle: the child the sprite's <c>muzzle</c> socket places, on whichever frame is drawn.
    /// Its <see cref="Entity.WorldPosition"/> is where a bolt leaves from.
    /// </summary>
    public Entity Muzzle => _visual.Muzzle;

    /// <summary>The body's edge in world units, and the frame's in texels: one texel per unit.</summary>
    private const int BodyPixels = 8;

    private static readonly Vector2 Body = new(BodyPixels, BodyPixels);

    // Authored bottom-centre in every frame, so a flip keeps the frame over the body and a squash
    // keeps its feet on the floor.
    private static readonly Vector2 FramePivot = CapsuleAssets.Sprites.Actors.Player.Frames.Idle0.Pivot;

    // Entity-specific components
    private readonly Visual _visual;
    private readonly KinematicBody2D _body;
    private readonly BoxCollider2D _hurtbox;
    private readonly AudioSource _footfall;
    private readonly PlayerTuning _tuning = PlayerTuning.Default;
    private readonly BoltTuning _bolt = BoltTuning.Default;

    private Vector2 _velocity;

    public Player(EntitySpawn spawn)
        : base(spawn)
    {
        Health = _tuning.MaxHealth;

        _visual = new Visual(this, FramePivot, _tuning);

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

        // The edges are this step's facts: the visual, stepping after this root, reads them once.
        JumpedThisStep = false;
        LandedThisStep = false;
        ShotThisStep = context.Input.WasPressed(GameInput.Shoot);

        // The body applies no forces: velocity is the game's, every step.
        _velocity.X = context.Input.Axis(GameInput.Move) * _tuning.WalkSpeed;
        _velocity.Y += _tuning.Gravity * delta;

        // IsOnFloor is state as of the last Move, so this reads the previous step's landing.
        bool wasOnFloor = _body.IsOnFloor;
        if (wasOnFloor && context.Input.WasPressed(GameInput.Jump))
        {
            _velocity.Y = -_tuning.JumpSpeed;
            JumpedThisStep = true;
            Log.Info("jumped");
        }

        _body.Move(_velocity * delta);

        if (_body.IsOnFloor)
        {
            _velocity.Y = 0f;

            if (!wasOnFloor)
            {
                LandedThisStep = true;
                _footfall.Play();
            }
        }

        if (_body.IsOnCeiling)
        {
            _velocity.Y = 0f;
        }
    }

    // The muzzle is a point on the frame drawn, and the frame is the visual's, stepped after this
    // root: the bolt leaves in the late step, from the socket of the frame this step settled on.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        if (!ShotThisStep)
        {
            return;
        }

        Scene!.Add(new Bolt(Muzzle.WorldPosition, _visual.Facing, _bolt));
        Log.Info("shot");
    }

    protected override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Current Health", Health);
        panel.Command("Heal", () => Health++);
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
    /// Everything the player looks like: the sprite, its animator, facing and squash-and-stretch,
    /// composed into this child's <see cref="Entity.Scale"/>. It reads what the root publishes and
    /// reacts; a child steps after its parent, so it reads this step's facts. The muzzle socket is
    /// placed under this child, so the facing scale mirrors it with the frame.
    /// </summary>
    private sealed class Visual : Entity
    {
        private readonly Player _player;
        private readonly PlayerTuning _tuning;
        private readonly SpriteAnimator _animator;

        private float _facing = 1f;
        private Vector2 _squash = Vector2.One;

        internal Visual(Player player, Vector2 pivot, PlayerTuning tuning)
            : base(player, pivot)
        {
            _player = player;
            _tuning = tuning;

            SpriteRenderer sprite = new(CapsuleAssets.Sprites.Actors.Player.Frames.Idle0);
            Add(sprite);
            Muzzle = sprite.Socket(CapsuleAssets.Sprites.Actors.Player.Sockets.Muzzle);

            _animator = new SpriteAnimator(sprite);
            Add(_animator);
        }

        /// <summary>The child the sprite places at its <c>muzzle</c> socket.</summary>
        internal Entity Muzzle { get; }

        /// <summary>The sign of the X the player faces along; the scale the frame is mirrored by.</summary>
        internal float Facing => _facing;

        /// <inheritdoc/>
        protected override void OnStep(in StepContext context)
        {
            float step = _tuning.ScaleRecovery * context.DeltaSeconds;
            Vector2 velocity = _player.Velocity;

            // Recovery runs before this step's impulses, so an impulse is drawn whole.
            _squash = new Vector2(Approach(_squash.X, 1f, step), Approach(_squash.Y, 1f, step));

            // Facing is kept through a standstill, so the player stops looking where it walked.
            if (velocity.X != 0f)
            {
                _facing = velocity.X < 0f ? -1f : 1f;
            }

            // Asked every step: the animator ignores the clip already playing, so the cycle runs
            // instead of restarting on frame 0.
            _animator.Play(velocity.X != 0f ? CapsuleAssets.Sprites.Actors.Player.Clips.Walk : CapsuleAssets.Sprites.Actors.Player.Clips.Idle);

            if (_player.JumpedThisStep)
            {
                _squash = _tuning.JumpStretch;
            }
            else if (_player.LandedThisStep)
            {
                _squash = _tuning.LandSquash;
            }

            Scale = new Vector2(_facing * _squash.X, _squash.Y);
        }

        // Towards the target by at most maxDelta, landing on it exactly.
        private static float Approach(float value, float target, float maxDelta) =>
            value > target ? MathF.Max(value - maxDelta, target) : MathF.Min(value + maxDelta, target);
    }
}

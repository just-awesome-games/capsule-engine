using System.Numerics;
using Capsule;
using Capsule.Animation;
using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Particles;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The walking, falling, jumping body the <c>player</c> document entries spawn.
/// <see cref="Entity.Position"/> is the top-left corner of the 8x8 body. The body collider is what
/// the <see cref="KinematicBody2D"/> sweeps and blocks on; the inset hurtbox blocks nothing and
/// reports <c>hazard</c> contacts alone. The body rides and is shoved by anything on the
/// <c>platform</c> layer, and being crushed kills. Everything visual hangs on the nested <see cref="Visual"/>
/// child, which reads the facts this root publishes; a shot leaves from the muzzle socket of the
/// frame that child draws, so the root fires in its late step, once the frame has settled. On
/// keyboard and mouse a shot aims at the pointer, and on a pad it flies along the facing. Its
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

    /// <summary>Steps left of the grace after a hit, during which hazards cost nothing.</summary>
    public int InvulnerableTicksLeft => _invulnerable.TicksLeft;

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
    private static readonly Vector2 FramePivot = CapsuleAssets.Sprites.Actors.PlayerSheet.Frames.Idle0.Pivot;

    // Entity-specific components
    private readonly Visual _visual;
    private readonly KinematicBody2D _body;
    private readonly BoxCollider2D _hurtbox;
    private readonly AudioSource _footfall;
    private readonly ParticleEmitter _dust;
    private readonly PlayerTuning _tuning = PlayerTuning.Default;
    private readonly BoltTuning _bolt = BoltTuning.Default;
    private readonly EntityPool<SparkBurst> _sparks = new(() => new SparkBurst(), capacity: 8);
    private readonly EntityPool<Bolt> _bolts;

    private Vector2 _velocity;
    private Countdown _invulnerable;

    public Player(EntitySpawn spawn)
        : base(spawn)
    {
        _bolts = new EntityPool<Bolt>(() => new Bolt(_sparks), capacity: 8);

        Health = _tuning.MaxHealth;

        _visual = new Visual(this, FramePivot, _tuning);

        BoxCollider2D bodyCollider = new(Body);
        Add(bodyCollider);

        // Grounded walks the hill at the speed it is given and follows the ground down its far side.
        _body = new KinematicBody2D(bodyCollider) { Mode = BodyMode.Grounded };
        _body.BlocksOn(CollisionLayers.Blocking);
        _body.MovedBy(CollisionLayers.Platform);
        _body.Crushed += OnCrushed;
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

        _footfall = new AudioSource(CapsuleAssets.Audio.StepSoftSound) { Bus = AudioBuses.Sfx };
        Add(_footfall);

        // A burst on landing, from the body's bottom centre: the pattern for a one-shot effect tied
        // to an entity's own lifetime. On the root, it draws under the Visual child at the same key.
        _dust = new ParticleEmitter(Sprite.White, capacity: 32)
        {
            Offset = new Vector2(BodyPixels / 2f, BodyPixels),
            Lifetime = (0.2f, 0.4f),
            Speed = (20f, 40f),
            Direction = -Vector2.UnitY,
            Spread = 120f,
            Gravity = new Vector2(0f, 120f),
            Scale = (1f, 2f),
            ScaleOverLifetime = Curve.Linear(1f, 0f),
            Color = ColorRgba.FromHex("#a09682"),
        };
        Add(_dust);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        float delta = context.DeltaSeconds;

        _invulnerable.Step();

        // The edges are this step's facts: the visual, stepping after this root, reads them once.
        JumpedThisStep = false;
        LandedThisStep = false;
        ShotThisStep = context.Input.WasPressed(GameInput.Shoot);

        // The body applies no forces: velocity is the game's, every step.
        float move = context.Input.Axis(GameInput.Move);
        _velocity.X = move * WalkSpeed(move);
        _velocity.Y += _tuning.Gravity * delta;

        // IsOnFloor is state as of the last Move, so this reads the previous step's landing.
        bool wasOnFloor = _body.IsOnFloor;
        if (wasOnFloor && context.Input.WasPressed(GameInput.Jump))
        {
            if (context.Input.IsHeld(GameInput.Drop))
            {
                // Through the one-way ledge or platform underfoot. On solid ground it changes nothing.
                _body.DropThrough();
            }
            else
            {
                _velocity.Y = -_tuning.JumpSpeed;
                JumpedThisStep = true;
                Log.Info("jumped");
            }
        }

        _body.Move(_velocity * delta);

        if (_body.IsOnFloor)
        {
            _velocity.Y = 0f;

            if (!wasOnFloor)
            {
                LandedThisStep = true;
                _footfall.Play();
                Run.Rumble.Play(_tuning.LandRumbleLow, _tuning.LandRumbleHigh, _tuning.LandRumbleSeconds);
                _dust.Emit(6);
            }
        }

        if (_body.IsOnCeiling)
        {
            _velocity.Y = 0f;
            BreakBricksOverhead();
        }
    }

    // The body keeps the speed it is given along a slope, so how a climb feels is the game's call. A
    // floor facing the way the player walks is a descent and speeds the walk, and one facing back
    // slows it.
    private float WalkSpeed(float move)
    {
        if (!_body.IsOnFloor || move == 0f)
        {
            return _tuning.WalkSpeed;
        }

        float downhill = Vector2.Dot(_body.FloorNormal, new Vector2(MathF.Sign(move), 0f));

        return _tuning.WalkSpeed * (1f + (_tuning.SlopeSpeed * downhill));
    }

    // A brick struck from below breaks: terrain that changes at run time.
    private void BreakBricksOverhead()
    {
        foreach (ColliderContact2D contact in _body.MoveContacts)
        {
            if (contact.Normal.Y > 0f && contact.Tile is { Type: TileTypes.Brick } tile)
            {
                tile.Map.RemoveTile(tile.X, tile.Y);
                Scene.Add(_sparks.Take().Burst(contact.Point));
            }
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

        Vector2 muzzle = Muzzle.WorldPosition;
        Scene.Add(_bolts.Take().Fire(muzzle, Aim(context.Input, muzzle), _bolt));
        Log.Info("shot");
    }

    // The pointer is in canvas pixels, and the camera maps it onto the world of the frame the player
    // clicked on. A pointer on the muzzle itself names no direction, so the shot keeps the facing.
    private Vector2 Aim(InputState input, Vector2 muzzle)
    {
        if (input.ActiveDevice == InputDevice.KeyboardMouse)
        {
            Vector2 aim = Scene.Camera.CanvasToWorld(input.Pointer) - muzzle;
            if (aim.LengthSquared() > 0f)
            {
                return Vector2.Normalize(aim);
            }
        }

        return new Vector2(_visual.Facing, 0f);
    }

    protected override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Current Health", Health);
        panel.Field("Bolts active", _bolts.Active);
        panel.Command("Heal", () => Health++);
    }

    // Named methods rather than lambdas: a handler with a name is one a subclass or a reader can
    // find, and it can be detached by the same method group that subscribed it.
    private void OnHurtboxEntered(ColliderContact2D contact)
    {
        if (_invulnerable.IsRunning)
        {
            return;
        }

        Health = Math.Max(Health - 1, 0);
        _invulnerable.Start(_tuning.InvulnerableTicks);
        Run.Rumble.Play(_tuning.HurtRumble);
        Scene.Freeze(_tuning.HurtFreezeTicks);
        Scene.Camera.Shake(_tuning.HurtShake);
        Log.Info(FormattableString.Invariant($"entered {contact.LayerName} at {contact.Point}, health {Health}"));
    }

    private void OnHurtboxExited(ColliderContact2D contact) =>
        Log.Info(FormattableString.Invariant($"exited {contact.LayerName} at {contact.Point}"));

    // Crushing kills outright. The scene returns to the menu once health reads zero.
    private void OnCrushed(ColliderContact2D contact)
    {
        Health = 0;
        Log.Info("crushed");
    }

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
        private readonly Vector2 _pivot;
        private readonly SpriteAnimator _animator;

        private float _facing = 1f;
        private Vector2 _squash = Vector2.One;

        internal Visual(Player player, Vector2 pivot, PlayerTuning tuning)
            : base(player, pivot)
        {
            _player = player;
            _pivot = pivot;
            _tuning = tuning;

            SpriteRenderer sprite = new(CapsuleAssets.Sprites.Actors.PlayerSheet.Frames.Idle0);
            Add(sprite);
            Muzzle = sprite.Socket(CapsuleAssets.Sprites.Actors.PlayerSheet.Sockets.Muzzle);

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
            _animator.Play(velocity.X != 0f ? CapsuleAssets.Sprites.Actors.PlayerSheet.Clips.Walk : CapsuleAssets.Sprites.Actors.PlayerSheet.Clips.Idle);

            if (_player.JumpedThisStep)
            {
                _squash = _tuning.JumpStretch;
            }
            else if (_player.LandedThisStep)
            {
                _squash = _tuning.LandSquash;
            }

            Scale = new Vector2(_facing * _squash.X, _squash.Y);
            Position = _pivot + new Vector2(0f, FeetGap());
        }

        // A hit flashes white and fades, then the grace reads as a red blink. The root owns the rule
        // and this child owns the look. Read after contacts, so the step a hit lands on draws white
        // and the hitstop holds it.
        protected override void OnLateStep(in StepContext context)
        {
            int grace = _player.InvulnerableTicksLeft;
            int sinceHit = _tuning.InvulnerableTicks - grace;

            Flash = grace > 0 ? 1f - Math.Min(sinceHit / (float)_tuning.HurtFlashTicks, 1f) : 0f;
            Tint = grace > 0 ? _tuning.HurtTint : ColorRgba.White;
            Visible = Flash > 0f || grace / _tuning.BlinkTicks % 2 == 0;
        }

        // The box rests on its corner on a slope, which leaves the bottom-centre in the air. The feet
        // are drawn onto the ground under the centre instead. Half the body's width reaches a 45 degree
        // slope's surface.
        private float FeetGap()
        {
            KinematicBody2D body = _player._body;
            Vector2 feet = _player.Position + new Vector2(BodyPixels / 2f, BodyPixels);
            float reach = (BodyPixels / 2f) + CollisionTolerance.ContactSkin;

            return body.IsOnFloor && Scene.Collision.Raycast(feet, Vector2.UnitY, reach, body.Filter, out RayHit2D hit, body.Collider.Handle)
                ? hit.Distance
                : 0f;
        }

        // Towards the target by at most maxDelta, landing on it exactly.
        private static float Approach(float value, float target, float maxDelta) =>
            value > target ? MathF.Max(value - maxDelta, target) : MathF.Min(value + maxDelta, target);
    }
}

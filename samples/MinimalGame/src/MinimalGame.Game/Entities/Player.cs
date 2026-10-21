using MinimalGame.Game.Tiles;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The 8x8 body the <c>player</c> document entries spawn, which walks, jumps and shoots, with its
/// top-left corner at <see cref="Entity.Position"/>. Everything visual hangs on the
/// <see cref="PlayerVisual"/> child, which steps after this root and reads the facts it publishes.
/// </summary>
public sealed class Player : Entity
{
    /// <summary>The body's edge in world units, and the frame's in texels: one texel per unit.</summary>
    private const int BodyPixels = 8;

    // A countdown is stepped at the top of each step, before this step can start one. The extra tick
    // keeps the step that starts a grace from spending any of it.
    private const int StartingStep = 1;

    private static readonly Vector2 Body = new(BodyPixels, BodyPixels);

    // Authored bottom-centre in every frame. A flip keeps the frame over the body, and a squash keeps
    // its feet on the floor.
    private static readonly Vector2 FramePivot = CapsuleAssets.Sprites.Actors.PlayerSheet.Frames.Idle0.Pivot;

    private readonly PlayerVisual _visual;
    private readonly KinematicBody2D _body;
    private readonly BoxCollider2D _hurtbox;
    private readonly AudioSource _footfall;
    private readonly ParticleEmitter _dust;
    private readonly ParticleEmitter _puff;
    private readonly BoltTuning _bolt = BoltTuning.Default;

    [Authorable]
    private PlayerTuning _tuning = PlayerTuning.Default;

    private Vector2 _velocity;

    // The sideways speed of the floor the player last left, which it keeps until it lands.
    private float _drift;

    private Countdown _invulnerable;
    private Countdown _coyote;
    private Countdown _jumpBuffer;

    /// <summary>What is left of <see cref="PlayerTuning.MaxHealth"/>: one spent per hazard contact, never below zero.</summary>
    public int Health { get; private set; }

    /// <summary>Whether the last step took off from the floor, cleared as each step begins.</summary>
    public bool JumpedThisStep { get; private set; }

    /// <summary>Whether the last step landed on a floor, cleared as each step begins.</summary>
    public bool LandedThisStep { get; private set; }

    /// <summary>Whether the last step fired a bolt, cleared as each step begins.</summary>
    public bool ShotThisStep { get; private set; }

    /// <summary>Whether input moves, jumps and shoots, defaulting to true. A cutscene clears it while it plays.</summary>
    public bool HasControl { get; set; } = true;

    /// <summary>The push, in world units per second, that the move adds to the player's own velocity, such as a gust's.</summary>
    public Vector2 Wind { get; set; }

    /// <summary>The gravity the player falls by in place of its tuning's, such as a waterfall's, or null for its own.</summary>
    public float? GravityOverride { get; set; }

    public Player(EntitySpawn spawn)
        : base(spawn)
    {
        Health = _tuning.MaxHealth;

        _visual = new PlayerVisual(this, FramePivot, _tuning);

        BoxCollider2D bodyCollider = new(Body) { Layer = CollisionLayers.Player };
        Add(bodyCollider);

        // Grounded walks the hill at the speed it is given and follows the ground down its far side. The
        // step height walks it over the curb without a jump. Resting on the center stands the player's
        // feet on the hill instead of on the corner of its box.
        _body = new KinematicBody2D(bodyCollider)
        {
            Mode = BodyMode.Grounded,
            StepHeight = _tuning.StepHeight,
            RestsOnCenter = true,
            BlockedBy = CollisionLayers.Blocking,
            MovedBy = new(CollisionLayers.Platform),
        };
        _body.Crushed += OnCrushed;
        Add(_body);

        float hurtboxEdge = BodyPixels - (_tuning.HurtboxInset * 2);
        _hurtbox = new BoxCollider2D(new Vector2(hurtboxEdge, hurtboxEdge))
        {
            Offset = new Vector2(_tuning.HurtboxInset, _tuning.HurtboxInset),
            ReportsContacts = true,
            Detects = new(CollisionLayers.Hazard),
        };
        _hurtbox.ContactEntered += OnHurtboxEntered;
        _hurtbox.ContactExited += OnHurtboxExited;
        Add(_hurtbox);

        _footfall = new AudioSource(CapsuleAssets.Audio.StepSoftSound) { Bus = AudioBuses.Sfx };
        Add(_footfall);

        // A burst on landing, from the body's bottom centre: the pattern for a one-shot effect tied
        // to an entity's own lifetime. On the root, it draws under the PlayerVisual child at the same key.
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

        // The dust a jump kicks up where it leaves the ground: one puff that plays its clip at the clip's own
        // steps and ends when the clip does.
        _puff = new ParticleEmitter(CapsuleAssets.Sprites.Effects.PuffSheet.Clips.Puff, capacity: 4)
        {
            Offset = new Vector2(BodyPixels / 2f, BodyPixels),
        };
        Add(_puff);
    }

    /// <summary>The levers this player runs on, fixed for its lifetime. A placement names one as <c>"tuning": "default"</c>.</summary>
    public PlayerTuning Tuning => _tuning;

    /// <summary>The velocity the body moves at, in world units per second, as of the last step.</summary>
    public Vector2 Velocity => _velocity;

    /// <summary>Whether the body stood on a floor after the last step.</summary>
    public bool IsOnFloor => _body.IsOnFloor;

    /// <summary>Steps left of the grace after a hit, during which hazards cost nothing.</summary>
    public int InvulnerableTicksLeft => _invulnerable.TicksLeft;

    /// <summary>
    /// The muzzle: the child the sprite's <c>muzzle</c> socket places, on whichever frame is drawn.
    /// Its <see cref="Entity.WorldPosition"/> is where a bolt leaves from.
    /// </summary>
    public Entity Muzzle => _visual.Muzzle;

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        float delta = context.DeltaSeconds;

        _invulnerable.Step();
        _coyote.Step();
        _jumpBuffer.Step();

        // The edges are this step's facts. The visual steps after this root and reads them once.
        JumpedThisStep = false;
        LandedThisStep = false;
        ShotThisStep = HasControl && context.Input.WasPressed(GameInput.Shoot);

        // The body applies no forces. Velocity is the game's, every step.
        float move = HasControl ? context.Input.Axis(GameInput.Move) : 0f;
        float target = move * SlopedWalkSpeed(move);
        _velocity.X = IceUnderfoot() is { } ice ? MoveTowards(_velocity.X, target, ice.Friction * delta) : target;

        _velocity.Y += (GravityOverride ?? _tuning.Gravity) * delta;

        // IsOnFloor is state as of the last Move, which is the previous step's landing. The floor's
        // speed is read before the move that may leave it.
        bool wasOnFloor = _body.IsOnFloor;
        Vector2 platform = _body.PlatformVelocity;
        bool pressed = HasControl && context.Input.WasPressed(GameInput.Jump);
        bool dropped = false;
        if (pressed && context.Input.IsHeld(GameInput.Drop))
        {
            // Through the one-way ledge or platform underfoot. On solid ground it changes nothing.
            if (wasOnFloor)
            {
                _body.DropThrough();
                dropped = true;
            }
        }
        else
        {
            // A press is kept a few steps for the landing after it, and a ledge walked off keeps a few steps
            // of floor.
            if (pressed)
            {
                _jumpBuffer.Start(_tuning.JumpBufferTicks + StartingStep);
            }

            if (HasControl && _jumpBuffer.IsRunning && (wasOnFloor || _coyote.IsRunning))
            {
                _jumpBuffer.Stop();
                _coyote.Stop();
                _velocity.Y = -_tuning.JumpSpeed;
                JumpedThisStep = true;
                _puff.Emit(1);
                Log.Info("jumped");
            }
        }

        // The drift and the wind push on top of the player's own velocity, which keeps neither.
        _body.Move((_velocity + new Vector2(_drift, 0f) + Wind) * delta);

        if (_body.IsOnFloor)
        {
            float fall = _velocity.Y;
            _velocity.Y = 0f;
            _drift = 0f;

            if (!wasOnFloor)
            {
                LandedThisStep = true;
                _footfall.Play();
                Run.Rumble.Play(_tuning.LandRumbleLow, _tuning.LandRumbleHigh, _tuning.LandRumbleSeconds);
                _dust.Emit(6);
                if (fall >= _tuning.HardLandingSpeed)
                {
                    Scene.Camera.Shake(_tuning.HardLandingShake);
                }
            }
        }
        else if (wasOnFloor)
        {
            // Leaving a moving floor keeps its speed: all of a sideways ride, and only the rise of a lift.
            _drift = platform.X;
            _velocity.Y += MathF.Min(platform.Y, 0f);
            if (!JumpedThisStep && !dropped)
            {
                _coyote.Start(_tuning.CoyoteTicks + StartingStep);
            }
        }

        if (_body.IsOnCeiling)
        {
            _velocity.Y = 0f;
            BreakBricksOverhead();
        }
    }

    // The body keeps the horizontal speed it is given on a slope. The game decides how a climb feels.
    // A floor facing the way the player walks is a descent and speeds the walk. One facing back slows
    // it.
    private float SlopedWalkSpeed(float move)
    {
        if (!_body.IsOnFloor || move == 0f)
        {
            return _tuning.WalkSpeed;
        }

        float downhill = Vector2.Dot(_body.FloorNormal, new Vector2(MathF.Sign(move), 0f));

        return _tuning.WalkSpeed * (1f + (_tuning.SlopeSpeed * downhill));
    }

    // The ice the last move stood on, or null on any other ground and in the air.
    private Ice? IceUnderfoot()
    {
        foreach (ColliderContact2D contact in _body.MoveContacts)
        {
            if (_body.ClassifyNormal(contact.Normal) == SurfaceKind.Floor && contact.Tile is { Type: Ice ice })
            {
                return ice;
            }
        }

        return null;
    }

    // Steps current toward target by at most maxStep, landing on it exactly.
    internal static float MoveTowards(float current, float target, float maxStep) =>
        MathF.Abs(target - current) <= maxStep ? target : current + (MathF.Sign(target - current) * maxStep);

    // A brick struck from below breaks.
    private void BreakBricksOverhead()
    {
        foreach (ColliderContact2D contact in _body.MoveContacts)
        {
            if (_body.ClassifyNormal(contact.Normal) == SurfaceKind.Ceiling && contact.Tile is { Type: Brick } tile)
            {
                tile.Map.RemoveTile(tile.X, tile.Y);
                Scene.Add(Scene.Pool<SparkBurst>().Take().Burst(contact.Point));
            }
        }
    }

    // The muzzle is a point on the frame the visual draws, and the visual steps after this root. The
    // bolt leaves in the late step, from the socket of the frame this step settled on. On keyboard and
    // mouse it aims at the pointer, and on a pad it flies along the facing.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        if (!ShotThisStep)
        {
            return;
        }

        Vector2 muzzle = Muzzle.WorldPosition;
        Scene.Add(Scene.Pool<Bolt>().Take().Fire(muzzle, Aim(context.Input, muzzle), _bolt));
        Log.Info("shot");
    }

    // The pointer is in canvas pixels, and the camera maps it onto the world of the frame the player
    // clicked on. A pointer on the muzzle itself names no direction, and the shot keeps the facing.
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

    // The player declares what it takes from the scene's pools, which preloads them: its bolts and the
    // sparks it shares with its bolts.
    /// <inheritdoc/>
    protected override void CollectAssets(AssetCollection assets)
    {
        assets.Pool<Bolt>(capacity: 8);
        assets.Pool<SparkBurst>(capacity: 2);
    }

    protected override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Current Health", Health);
        panel.Field("Bolts active", Scene.Pool<Bolt>().Active);
        panel.Command("Heal", () => Health++);
    }

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
}

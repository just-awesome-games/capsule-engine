namespace MinimalGame.Game.Entities;

/// <summary>
/// Everything the <see cref="Player"/> looks like: the sprite, the cap drawn over it, their animator, the facing and
/// squash-and-stretch composed into this child's <see cref="Entity.Scale"/>, the walk cycle's footsteps, and the
/// draw-in from the feet up on arriving in a room.
/// It steps after its parent and reads the facts the parent published this step.
/// </summary>
internal sealed class PlayerVisual : Entity
{
    // The top of the cap above the feet, in texels: the height the draw-in climbs to before the
    // stretch scales it.
    private const float FigureHeight = 10f;

    // reveal.fx's parameter: how far above the feet the figure is drawn so far.
    private const string RevealHeight = "Reveal";

    private readonly Player _player;
    private readonly PlayerTuning _tuning;
    private readonly SpriteRenderer _cap;
    private readonly SpriteAnimator _animator;
    private readonly AudioSource _footstep;

    // One material, worn by the sprite and the cap alike through this entity's Material, and only
    // while the draw-in runs.
    private readonly Material _reveal = new(CapsuleAssets.Shaders.RevealShader);

    private float _facing = 1f;
    private Vector2 _squash = Vector2.One;
    private float _revealLeft;

    /// <summary>The child the sprite places at its <c>muzzle</c> socket.</summary>
    internal Entity Muzzle { get; }

    internal PlayerVisual(Player player, Vector2 pivot, PlayerTuning tuning)
        : base(player, pivot)
    {
        _player = player;
        _tuning = tuning;

        SpriteRenderer sprite = Add(new SpriteRenderer(CapsuleAssets.Sprites.Actors.PlayerSheet.Frames.Idle0));

        // The socket sits under this child, and the facing scale mirrors it with the frame.
        Muzzle = sprite.Socket(CapsuleAssets.Sprites.Actors.PlayerSheet.Sockets.Muzzle);

        // The cap is its sheet's drawing of whatever frame the sprite shows, drawn over it. The one
        // animator writes both, and the cap never lags a frame behind.
        _cap = new SpriteRenderer(sprite.Sprite)
        {
            Variant = CapsuleAssets.Sprites.Actors.PlayerCapSheet.Variant,
        };
        Add(_cap);

        _animator = Add(new SpriteAnimator(sprite, _cap));

        _footstep = Add(new AudioSource(CapsuleAssets.Audio.StepSoftSound) { Bus = AudioBuses.Sfx });
    }

    /// <summary>The sign of the X the player faces along, which mirrors the frame.</summary>
    internal float Facing => _facing;

    /// <summary>Whether the cap is drawn over the sprite.</summary>
    internal bool WearsCap
    {
        get => _cap.Visible;
        set => _cap.Visible = value;
    }

    /// <summary>Draws the player in from the feet up over <see cref="PlayerTuning.ArrivalRevealSeconds"/>.</summary>
    internal void DrawIn()
    {
        if (_tuning.ArrivalRevealSeconds <= 0f)
        {
            return;
        }

        _revealLeft = _tuning.ArrivalRevealSeconds;
        _reveal.Set(RevealHeight, 0f);
        Material = _reveal;
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        float step = _tuning.ScaleRecovery * context.DeltaSeconds;
        Vector2 velocity = _player.Velocity;

        // Recovery runs before this step's impulses, and an impulse is drawn whole.
        _squash = new Vector2(DeterministicMath.MoveToward(_squash.X, 1f, step), DeterministicMath.MoveToward(_squash.Y, 1f, step));

        // Facing is kept through a standstill. The player stops looking the way it walked.
        if (velocity.X != 0f)
        {
            _facing = velocity.X < 0f ? -1f : 1f;
        }

        // Airborne, the clip holds on its frame. The grounded clip is chosen again on landing.
        _animator.Paused = !_player.IsOnFloor;
        if (!_animator.Paused)
        {
            // Asked every step. The animator ignores the clip already playing, and the cycle runs on
            // instead of restarting on frame 0.
            bool walking = velocity.X != 0f;
            _animator.Play(walking ? CapsuleAssets.Sprites.Actors.PlayerSheet.Clips.Walk : CapsuleAssets.Sprites.Actors.PlayerSheet.Clips.Idle);

            // The stride keeps pace with the ground covered. A downhill run or an ice slide plays
            // it faster.
            _animator.Speed = walking ? MathF.Abs(velocity.X) / _tuning.WalkSpeed : 1f;
        }

        if (_player.JumpedThisStep)
        {
            _squash = _tuning.JumpStretch;
        }
        else if (_player.LandedThisStep)
        {
            _squash = _tuning.LandSquash;
        }

        Scale = new Vector2(_facing * _squash.X, _squash.Y);

        if (_revealLeft > 0f)
        {
            // The shader measures in drawn units, which the stretch scales along with the figure.
            _revealLeft -= context.DeltaSeconds;
            float drawn = 1f - (_revealLeft / _tuning.ArrivalRevealSeconds);
            _reveal.Set(RevealHeight, drawn * FigureHeight * _squash.Y);
            Material = _revealLeft > 0f ? _reveal : null;
        }
    }

    // The draw-in's material is set after the room starts, and declaring it here loads it with the room.
    /// <inheritdoc/>
    protected override void CollectAssets(AssetCollection assets) => assets.Add(_reveal);

    // A hit flashes white and fades, then the grace reads as a red blink. The root owns the rule
    // and this child owns the look. The late step follows contacts: the step a hit lands on draws
    // white, and the hitstop holds it. It also follows the animator: a footstep sounds on the step
    // its frame is drawn.
    protected override void OnLateStep(in StepContext context)
    {
        if (_animator.Reached(CapsuleAssets.Sprites.Actors.PlayerSheet.Events.Footstep))
        {
            _footstep.Play();
        }

        int grace = _player.InvulnerableTicksLeft;
        int sinceHit = _tuning.InvulnerableTicks - grace;

        Flash = grace > 0 ? 1f - Math.Min(sinceHit / (float)_tuning.HurtFlashTicks, 1f) : 0f;
        Tint = grace > 0 ? _tuning.HurtTint : ColorRgba.White;
        Visible = Flash > 0f || grace / _tuning.BlinkTicks % 2 == 0;
    }
}

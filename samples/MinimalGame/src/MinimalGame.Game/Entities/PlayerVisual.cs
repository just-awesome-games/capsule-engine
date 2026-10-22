namespace MinimalGame.Game.Entities;

/// <summary>
/// Everything the <see cref="Player"/> looks like: the sprite, the cap drawn over it, their animator, the facing and
/// squash-and-stretch composed into this child's <see cref="Entity.Scale"/>, and the walk cycle's footsteps.
/// It steps after its parent and reads the facts the parent published this step.
/// </summary>
internal sealed class PlayerVisual : Entity
{
    private readonly Player _player;
    private readonly PlayerTuning _tuning;
    private readonly SpriteRenderer _cap;
    private readonly SpriteAnimator _animator;
    private readonly AudioSource _footstep;

    private float _facing = 1f;
    private Vector2 _squash = Vector2.One;

    /// <summary>The child the sprite places at its <c>muzzle</c> socket.</summary>
    internal Entity Muzzle { get; }

    internal PlayerVisual(Player player, Vector2 pivot, PlayerTuning tuning)
        : base(player, pivot)
    {
        _player = player;
        _tuning = tuning;

        SpriteRenderer sprite = new(CapsuleAssets.Sprites.Actors.PlayerSheet.Frames.Idle0);
        Add(sprite);

        // The socket sits under this child, and the facing scale mirrors it with the frame.
        Muzzle = sprite.Socket(CapsuleAssets.Sprites.Actors.PlayerSheet.Sockets.Muzzle);

        // The cap is its sheet's drawing of whatever frame the sprite shows, drawn over it. The one
        // animator writes both, and the cap never lags a frame behind.
        _cap = new SpriteRenderer(sprite.Sprite)
        {
            Variant = CapsuleAssets.Sprites.Actors.PlayerCapSheet.Variant,
        };
        Add(_cap);

        _animator = new SpriteAnimator(sprite, _cap);
        Add(_animator);

        _footstep = new AudioSource(CapsuleAssets.Audio.StepSoftSound) { Bus = AudioBuses.Sfx };
        Add(_footstep);
    }

    /// <summary>The sign of the X the player faces along, which mirrors the frame.</summary>
    internal float Facing => _facing;

    /// <summary>Whether the cap is drawn over the sprite.</summary>
    internal bool WearsCap
    {
        get => _cap.Visible;
        set => _cap.Visible = value;
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        float step = _tuning.ScaleRecovery * context.DeltaSeconds;
        Vector2 velocity = _player.Velocity;

        // Recovery runs before this step's impulses, and an impulse is drawn whole.
        _squash = new Vector2(Player.MoveTowards(_squash.X, 1f, step), Player.MoveTowards(_squash.Y, 1f, step));

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
    }

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

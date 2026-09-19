namespace Capsule.Animation;

/// <summary>
/// A tick cursor over one eased run from 0 to 1. Its owner holds it, steps it each step, and writes
/// <see cref="Value"/> wherever it belongs.
/// <para>
/// This is a mutable value. Copying it copies the position, and the copy steps independently.
/// </para>
/// </summary>
/// <example>
/// <code>
/// _knockback.Start(12, Ease.OutQuad);
/// ...
/// _knockback.Step();
/// Position = Vector2.Lerp(_struckAt, _restsAt, _knockback.Value);
/// if (_knockback.JustFinished)
/// {
///     _state = State.Idle;
/// }
/// </code>
/// </example>
public struct Tween
{
    /// <summary>
    /// Ticks spent since <see cref="Start"/>, capped at <see cref="Duration"/>. A
    /// <see cref="TweenLoop.PingPong"/> is capped at twice the duration, and its second half counts
    /// the swing home.
    /// </summary>
    public int TicksElapsed { get; private set; }

    /// <summary>The ticks the run takes end to end, as <see cref="Start"/> was last given. Zero before the first start.</summary>
    public int Duration { get; private set; }

    /// <summary>The curve <see cref="Value"/> is read through, as <see cref="Start"/> was last given it.</summary>
    public Ease Ease { get; private set; }

    /// <summary>What the run does with the tick after its last, as <see cref="Start"/> was last given.</summary>
    public TweenLoop Loop { get; private set; }

    /// <summary>
    /// Passes completed since <see cref="Start"/>, one per <see cref="Duration"/> ticks a loop runs.
    /// A <see cref="TweenLoop.Repeat"/> counts wraps and a <see cref="TweenLoop.PingPong"/> counts
    /// ends reached. A finished <see cref="TweenLoop.Once"/> reads 1.
    /// </summary>
    public int Passes { get; private set; }

    /// <summary>
    /// Whether the run has spent its last tick or been stopped. False before the first start, and
    /// always false on a looping run.
    /// </summary>
    public bool IsFinished { get; private set; }

    /// <summary>
    /// Whether the last <see cref="Step"/> ended a pass. It holds for that one step and the next step
    /// clears it. A pass ends on the final tick of a <see cref="TweenLoop.Once"/>, on the step that
    /// wrapped a <see cref="TweenLoop.Repeat"/>, and at either end of a
    /// <see cref="TweenLoop.PingPong"/> swing. <see cref="Seek"/> and <see cref="Stop"/> do not raise
    /// it.
    /// </summary>
    public bool JustFinished { get; private set; }

    /// <summary>Whether the tween has started and has ticks still to spend.</summary>
    public readonly bool IsRunning => Duration > 0 && !IsFinished;

    /// <summary>
    /// The eased position of the run. It reads <c>0</c> while no tick is spent or before the first
    /// start, and <c>1</c> once finished or at the far end of a swing, whatever the curve. An
    /// overshooting curve leaves <c>[0, 1]</c> in between. A <see cref="TweenLoop.Repeat"/> never
    /// reads 1, because the tick that would is the 0 its next pass opens on.
    /// </summary>
    public readonly float Value
    {
        get
        {
            if (Duration == 0)
            {
                return 0f;
            }

            // Only a ping-pong runs past its duration, and there the rest of the period is the swing home.
            int tick = TicksElapsed <= Duration ? TicksElapsed : Duration + Duration - TicksElapsed;

            return Easing.Apply(Ease, tick / (float)Duration);
        }
    }

    /// <summary>
    /// Starts a run of <paramref name="ticks"/> steps on <paramref name="ease"/>, discarding
    /// whatever the tween was running. <see cref="Value"/> is 0 until the first <see cref="Step"/>
    /// and <see cref="Passes"/> begins again at 0.
    /// </summary>
    /// <param name="ticks">Fixed steps one pass of the run takes. At least one.</param>
    /// <param name="ease">The curve to read the run through. Linear by default.</param>
    /// <param name="loop">What to do with the tick after the last. Finish by default.</param>
    /// <exception cref="ArgumentOutOfRangeException">The loop mode is not a declared <see cref="TweenLoop"/>.</exception>
    public void Start(int ticks, Ease ease = Ease.Linear, TweenLoop loop = TweenLoop.Once)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);

        if (loop is < TweenLoop.Once or > TweenLoop.PingPong)
        {
            throw new ArgumentOutOfRangeException(nameof(loop), loop, "No such loop mode.");
        }

        Duration = ticks;
        Ease = ease;
        Loop = loop;
        TicksElapsed = 0;
        Passes = 0;
        IsFinished = false;
        JustFinished = false;
    }

    /// <summary>
    /// Spends one tick of the run, wrapping where the <see cref="TweenLoop"/> says to, and clears a
    /// <see cref="JustFinished"/> raised by the previous step. On a finished or unstarted tween it
    /// only clears that flag.
    /// </summary>
    public void Step()
    {
        JustFinished = false;

        if (IsFinished || Duration == 0)
        {
            return;
        }

        TicksElapsed++;

        switch (Loop)
        {
            case TweenLoop.Once:
                if (TicksElapsed >= Duration)
                {
                    TicksElapsed = Duration;
                    Passes = 1;
                    IsFinished = true;
                    JustFinished = true;
                }

                break;

            case TweenLoop.Repeat:
                if (TicksElapsed >= Duration)
                {
                    TicksElapsed = 0;
                    Passes++;
                    JustFinished = true;
                }

                break;

            // A ping-pong ends a pass twice per period, at the far end and again at the wrap home.
            default:
                if (TicksElapsed >= Duration + Duration)
                {
                    TicksElapsed = 0;
                    Passes++;
                    JustFinished = true;
                }
                else if (TicksElapsed == Duration)
                {
                    Passes++;
                    JustFinished = true;
                }

                break;
        }
    }

    /// <summary>
    /// Ends the run at once. <see cref="Value"/> reads the end of the curve, no step reports a
    /// finish, and <see cref="Duration"/> is kept for a restart.
    /// </summary>
    public void Stop()
    {
        TicksElapsed = Duration;
        IsFinished = Duration > 0;
        JustFinished = false;
    }

    /// <summary>
    /// Positions the run where <paramref name="tick"/> calls to <see cref="Step"/> would leave it,
    /// <see cref="Passes"/> included. Tick 0 is the state <see cref="Start"/> left. A looping run
    /// takes the tick within its period. On a <see cref="TweenLoop.Once"/>, a tick at or past
    /// <see cref="Duration"/> gives the finished run.
    /// </summary>
    /// <param name="tick">Ticks into the run. Must not be negative.</param>
    /// <exception cref="InvalidOperationException">The tween has never been started, so it has no run to seek.</exception>
    public void Seek(int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        if (Duration == 0)
        {
            throw new InvalidOperationException("Tween has no run to seek. Call Start before Seek.");
        }

        JustFinished = false;

        if (Loop == TweenLoop.Once)
        {
            TicksElapsed = Math.Min(tick, Duration);
            IsFinished = TicksElapsed == Duration;
            Passes = IsFinished ? 1 : 0;

            return;
        }

        // A pass is one duration in both modes, so the count is the same division. Only the period the
        // position wraps within differs.
        TicksElapsed = tick % (Loop == TweenLoop.Repeat ? Duration : Duration + Duration);
        Passes = tick / Duration;
        IsFinished = false;
    }
}

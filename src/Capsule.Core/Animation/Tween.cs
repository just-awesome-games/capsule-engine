namespace Capsule.Animation;

/// <summary>
/// A tick cursor over one eased run from 0 to 1, held and stepped by its owner, which reads
/// <see cref="Value"/> each step and writes it wherever it belongs. <see cref="Start"/> puts
/// <see cref="Value"/> at 0 on the step it is called and exactly <see cref="Duration"/> steps later
/// it is 1. A mutable value: copying it copies the position, and a copy is stepped independently.
/// <para>
/// An owner starts a run where something begins — <c>_knockback.Start(12, Ease.OutQuad)</c> on a hit
/// — then steps and applies it, chaining the next run on <see cref="JustFinished"/>. A run that
/// repeats or swings back instead of ending says so with a <see cref="TweenLoop"/>:
/// <code>
/// _knockback.Step();
/// Position = Vector2.Lerp(_struckAt, _restsAt, _knockback.Value);
/// if (_knockback.JustFinished)
/// {
///     _state = State.Idle;
/// }
/// </code>
/// </para>
/// </summary>
public struct Tween
{
    /// <summary>
    /// Ticks spent since <see cref="Start"/>, never past <see cref="Duration"/> — nor past twice it
    /// on a <see cref="TweenLoop.PingPong"/>, whose second half counts the swing home.
    /// </summary>
    public int TicksElapsed { get; private set; }

    /// <summary>
    /// The ticks the run takes end to end, as <see cref="Start"/> was last given. Zero on a tween
    /// that has never started.
    /// </summary>
    public int Duration { get; private set; }

    /// <summary>The curve <see cref="Value"/> is read through; as <see cref="Start"/> was last given it.</summary>
    public Ease Ease { get; private set; }

    /// <summary>What the run does with the tick after its last; as <see cref="Start"/> was last given it.</summary>
    public TweenLoop Loop { get; private set; }

    /// <summary>
    /// Passes completed since <see cref="Start"/>, one every <see cref="Duration"/> ticks a loop
    /// runs: wraps on a <see cref="TweenLoop.Repeat"/> and ends reached on a
    /// <see cref="TweenLoop.PingPong"/>. 1 on a finished <see cref="TweenLoop.Once"/>.
    /// </summary>
    public int Passes { get; private set; }

    /// <summary>
    /// Whether the run has spent its last tick. False on a tween that has never started, and on a
    /// looping one always, since neither ends.
    /// </summary>
    public bool IsFinished { get; private set; }

    /// <summary>
    /// Whether the last <see cref="Step"/> ended a pass: true across that one step and cleared by the
    /// next. That is the final tick of a <see cref="TweenLoop.Once"/>, the step that wrapped on a
    /// <see cref="TweenLoop.Repeat"/>, and either end of the swing on a
    /// <see cref="TweenLoop.PingPong"/>. Never raised by <see cref="Seek"/>.
    /// </summary>
    public bool JustFinished { get; private set; }

    /// <summary>Whether the tween has started and has ticks still to spend.</summary>
    public readonly bool IsRunning => Duration > 0 && !IsFinished;

    /// <summary>
    /// The eased position of the run: exactly <c>0</c> while no tick is spent or the tween never
    /// started, and exactly <c>1</c> once finished or at the far end of a swing, whatever the curve.
    /// An overshooting curve leaves <c>[0, 1]</c> in between, and a <see cref="TweenLoop.Repeat"/>
    /// never reads 1, since the tick that would is the 0 its next pass opens on.
    /// </summary>
    public readonly float Value
    {
        get
        {
            if (Duration == 0)
            {
                return 0f;
            }

            // Past the duration only on a ping-pong, where what is left of the period is the
            // distance still to come home.
            int tick = TicksElapsed <= Duration ? TicksElapsed : Duration + Duration - TicksElapsed;

            return Easing.Apply(Ease, tick / (float)Duration);
        }
    }

    /// <summary>
    /// Starts a run of <paramref name="ticks"/> steps on <paramref name="ease"/>, discarding whatever
    /// the tween was running. <see cref="Value"/> is 0 until the first <see cref="Step"/> and
    /// <see cref="Passes"/> begins again at 0.
    /// </summary>
    /// <param name="ticks">Fixed steps one pass of the run takes; at least one.</param>
    /// <param name="ease">The curve to read the run through; linear by default.</param>
    /// <param name="loop">What to do with the tick after the last; finish by default.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The tick count is not positive, or the loop mode is not a declared <see cref="TweenLoop"/>.
    /// </exception>
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
    /// <see cref="JustFinished"/> raised by the previous step, which is all it does to a finished
    /// tween or one that never started.
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

            // A ping-pong ends a pass twice a period: out at the far end, and home at the wrap.
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
    /// Positions the run where <paramref name="tick"/> calls to <see cref="Step"/> would leave it,
    /// <see cref="Passes"/> included: tick 0 is the state <see cref="Start"/> left, a looping run
    /// takes the tick within its period, and on a <see cref="TweenLoop.Once"/> a tick at or past
    /// <see cref="Duration"/> is the finished run. <see cref="JustFinished"/> is never raised.
    /// </summary>
    /// <param name="tick">Ticks into the run; not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tick is negative.</exception>
    /// <exception cref="InvalidOperationException">The tween has never been started, so it has no run to seek.</exception>
    public void Seek(int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        if (Duration == 0)
        {
            throw new InvalidOperationException("A tween is seeked within the run it was started on; start it first.");
        }

        JustFinished = false;

        if (Loop == TweenLoop.Once)
        {
            TicksElapsed = Math.Min(tick, Duration);
            IsFinished = TicksElapsed == Duration;
            Passes = IsFinished ? 1 : 0;

            return;
        }

        // A pass is one duration either way, so the count is the same division for both modes; what
        // differs is the period the position wraps within.
        TicksElapsed = tick % (Loop == TweenLoop.Repeat ? Duration : Duration + Duration);
        Passes = tick / Duration;
        IsFinished = false;
    }
}

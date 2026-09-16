namespace Capsule;

/// <summary>
/// A one-shot counter of fixed steps its owner holds and steps: a cooldown, a delay, a lockout.
/// <see cref="Start"/> arms it, each <see cref="Step"/> spends one tick, and the step that spends the
/// last raises <see cref="JustFinished"/> for that step alone.
/// <para>
/// A mutable value: copying it copies the ticks left, and a copy is stepped independently.
/// </para>
/// </summary>
public struct Countdown
{
    /// <summary>Ticks still to spend; zero when nothing is armed and once the last tick is spent.</summary>
    public int TicksLeft { get; private set; }

    /// <summary>
    /// The ticks <see cref="Start"/> was last given, kept once the countdown elapses or stops. Zero
    /// on a fresh countdown.
    /// </summary>
    public int Duration { get; private set; }

    /// <summary>
    /// Ticks spent since the last <see cref="Start"/>, reaching <see cref="Duration"/> once the
    /// countdown has finished and after a <see cref="Stop"/>, which leaves nothing to spend.
    /// </summary>
    public readonly int TicksElapsed => Duration - TicksLeft;

    /// <summary>Whether ticks remain to spend.</summary>
    public readonly bool IsRunning => TicksLeft > 0;

    /// <summary>
    /// Whether the last <see cref="Step"/> spent the final tick: true across that one step and
    /// cleared by the next. Never raised by <see cref="Stop"/> or by a <see cref="Start"/> of zero.
    /// </summary>
    public bool JustFinished { get; private set; }

    /// <summary>
    /// Arms the countdown for <paramref name="ticks"/> steps, discarding whatever it was counting and
    /// clearing <see cref="JustFinished"/>. Zero ticks is finished at once, and no step reports it.
    /// </summary>
    /// <param name="ticks">Fixed steps to count; not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tick count is negative.</exception>
    public void Start(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        Duration = ticks;
        TicksLeft = ticks;
        JustFinished = false;
    }

    /// <summary>
    /// Spends one tick where any remain, and clears a <see cref="JustFinished"/> raised by the
    /// previous step whether running, finished or stopped, which is all it does in those last two.
    /// </summary>
    public void Step()
    {
        JustFinished = false;

        if (TicksLeft == 0)
        {
            return;
        }

        TicksLeft--;
        JustFinished = TicksLeft == 0;
    }

    /// <summary>
    /// Disarms the countdown without finishing it: <see cref="TicksLeft"/> drops to zero, no step
    /// reports a finish, and <see cref="Duration"/> is left for a restart.
    /// </summary>
    public void Stop()
    {
        TicksLeft = 0;
        JustFinished = false;
    }
}

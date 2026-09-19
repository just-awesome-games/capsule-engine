namespace Capsule.Animation;

/// <summary>
/// A one-shot counter of fixed steps for a cooldown, a delay or a lockout. <see cref="Start"/> arms
/// it, each <see cref="Step"/> spends one tick, and the step that spends the last raises
/// <see cref="JustFinished"/> for that step alone.
/// <para>
/// This is a mutable value. Copying it copies the ticks left, and the copy steps independently.
/// </para>
/// </summary>
/// <example>
/// <code>
/// if (fired)
/// {
///     _cooldown.Start(30);
/// }
///
/// _cooldown.Step();
/// if (!_cooldown.IsRunning)
/// {
///     Fire();
/// }
/// </code>
/// </example>
public struct Countdown
{
    private Tween _run;

    /// <summary>The ticks the countdown takes end to end, as <see cref="Start"/> was last given. Zero before the first start.</summary>
    public readonly int Duration => _run.Duration;

    /// <summary>Ticks still to spend. Zero once the countdown has finished or been stopped.</summary>
    public readonly int TicksLeft => Duration - TicksElapsed;

    /// <summary>Ticks spent since <see cref="Start"/>, capped at <see cref="Duration"/>. A stop counts the rest as spent.</summary>
    public readonly int TicksElapsed => _run.TicksElapsed;

    /// <summary>Whether the countdown has started and has ticks still to spend.</summary>
    public readonly bool IsRunning => _run.IsRunning;

    /// <summary>
    /// Whether the last <see cref="Step"/> spent the final tick. It holds for that one step and the
    /// next step clears it. <see cref="Stop"/> does not raise it, and neither does a start of zero
    /// ticks.
    /// </summary>
    public readonly bool JustFinished => _run.JustFinished;

    /// <summary>
    /// Arms the countdown for <paramref name="ticks"/> steps, discarding whatever it was counting.
    /// Zero ticks is finished at once and no step reports it.
    /// </summary>
    /// <param name="ticks">Fixed steps to count. Must not be negative.</param>
    public void Start(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        if (ticks == 0)
        {
            _run = default;

            return;
        }

        _run.Start(ticks);
    }

    /// <summary>
    /// Spends one tick where any remain, and clears a <see cref="JustFinished"/> raised by the
    /// previous step. On a finished or unarmed countdown it only clears that flag.
    /// </summary>
    public void Step() => _run.Step();

    /// <summary>
    /// Disarms the countdown without finishing it. <see cref="TicksLeft"/> drops to zero, no step
    /// reports a finish, and <see cref="Duration"/> is kept for a restart.
    /// </summary>
    public void Stop() => _run.Stop();
}

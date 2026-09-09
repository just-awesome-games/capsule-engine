namespace Capsule.Animation;

/// <summary>
/// The tick cursor over an ordered run of frames, each held for a whole number of fixed steps. It
/// carries the position and nothing else; what a frame is belongs to whatever composes the cursor
/// with its own frame table. A fresh cursor is on frame 0 with no ticks elapsed. Every
/// <see cref="Step"/> advances exactly one tick, so a frame held for <c>n</c> ticks is current
/// across exactly <c>n</c> steps. A looping run wraps from the last frame back to frame 0; one
/// that does not loop holds its last frame and reports <see cref="IsFinished"/> once that frame's
/// ticks have elapsed, after which stepping does nothing.
/// <para>
/// A mutable value: copying it copies the position. <see cref="Restart"/> it when the run it walks
/// changes, since the cursor is meaningless against a different one.
/// </para>
/// </summary>
public struct AnimationPlayback
{
    /// <summary>The frame the cursor is on, from 0.</summary>
    public int FrameIndex { get; private set; }

    /// <summary>
    /// Ticks already spent on <see cref="FrameIndex"/>: zero on the step the frame became current,
    /// and never more than that frame's own duration.
    /// </summary>
    public int TicksElapsed { get; private set; }

    /// <summary>
    /// Whether a non-looping run has spent the last frame's ticks. A looping run never finishes.
    /// </summary>
    public bool IsFinished { get; private set; }

    /// <summary>Returns the cursor to frame 0 with no ticks elapsed and nothing finished.</summary>
    public void Restart()
    {
        FrameIndex = 0;
        TicksElapsed = 0;
        IsFinished = false;
    }

    /// <summary>
    /// Positions the cursor where a fresh cursor stepped <paramref name="tick"/> times over
    /// <paramref name="frameTicks"/> would stand: the frame that tick lands on, with the ticks
    /// already spent inside it, leaving that frame the rest of its own ticks to hold. Tick 0 is
    /// <see cref="Restart"/>. A looping run wraps the tick modulo the run's total ticks and never
    /// finishes; one that does not loop clamps a tick at or past its total to the last frame with
    /// that frame's ticks spent and <see cref="IsFinished"/> set.
    /// </summary>
    /// <param name="frameTicks">
    /// How many steps each frame is held for, in frame order; every duration positive.
    /// </param>
    /// <param name="loop">Whether the last frame wraps back to frame 0 instead of finishing.</param>
    /// <param name="tick">Ticks elapsed since the run began; not negative.</param>
    /// <exception cref="ArgumentException">The run is empty or holds a non-positive hold.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The tick is negative.</exception>
    public void Seek(ReadOnlySpan<int> frameTicks, bool loop, int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        if (frameTicks.IsEmpty)
        {
            throw new ArgumentException("a run has at least one frame.", nameof(frameTicks));
        }

        // Widened, because a run's ticks are per-frame ints whose total need not be one.
        long total = 0;
        for (int i = 0; i < frameTicks.Length; i++)
        {
            if (frameTicks[i] <= 0)
            {
                throw new ArgumentException(
                    $"frame {i} is held for {frameTicks[i]} ticks; every frame is held for at least one fixed step.",
                    nameof(frameTicks));
            }

            total += frameTicks[i];
        }

        long remaining = loop ? tick % total : tick;
        for (int i = 0; i < frameTicks.Length; i++)
        {
            if (remaining < frameTicks[i])
            {
                FrameIndex = i;
                TicksElapsed = (int)remaining;
                IsFinished = false;
                return;
            }

            remaining -= frameTicks[i];
        }

        FrameIndex = frameTicks.Length - 1;
        TicksElapsed = frameTicks[^1];
        IsFinished = true;
    }

    /// <summary>
    /// The ticks elapsed since the current pass over <paramref name="frameTicks"/> began: the ticks
    /// of every earlier frame plus <see cref="TicksElapsed"/> on the current one, which is the run's
    /// total ticks once it has finished. Passing it back to <see cref="Seek"/> over the same
    /// durations reproduces this position. A run whose ticks up to the cursor exceed
    /// <see cref="int.MaxValue"/> is outside that round trip and throws rather than reporting a
    /// tick <see cref="Seek"/> cannot take back.
    /// </summary>
    /// <param name="frameTicks">
    /// How many steps each frame is held for, in frame order; the run the cursor walks.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The run does not reach the cursor, or its ticks up to the cursor exceed
    /// <see cref="int.MaxValue"/>.
    /// </exception>
    public readonly int TickOf(ReadOnlySpan<int> frameTicks)
    {
        if (FrameIndex >= frameTicks.Length)
        {
            throw new ArgumentException(
                $"the cursor is on frame {FrameIndex} of a run of {frameTicks.Length}; a cursor reports its tick against the one run it walks.",
                nameof(frameTicks));
        }

        // Widened, because a run's ticks are per-frame ints whose total need not be one.
        long tick = TicksElapsed;
        for (int i = 0; i < FrameIndex; i++)
        {
            tick += frameTicks[i];
        }

        if (tick > int.MaxValue)
        {
            throw new ArgumentException(
                $"the cursor stands {tick} ticks into the run; a tick past {int.MaxValue} cannot be seeked back to.",
                nameof(frameTicks));
        }

        return (int)tick;
    }

    /// <summary>
    /// Advances the cursor by one fixed step over <paramref name="frameTicks"/>, and does nothing
    /// once a non-looping run has finished.
    /// </summary>
    /// <param name="frameTicks">
    /// How many steps each frame is held for, in frame order; the same run on every step, and every
    /// duration positive.
    /// </param>
    /// <param name="loop">Whether the last frame wraps back to frame 0 instead of finishing.</param>
    /// <exception cref="ArgumentException">The run is empty, does not reach the cursor, or holds a non-positive hold.</exception>
    public void Step(ReadOnlySpan<int> frameTicks, bool loop)
    {
        if (IsFinished)
        {
            return;
        }

        if (FrameIndex >= frameTicks.Length)
        {
            throw new ArgumentException(
                $"the cursor is on frame {FrameIndex} of a run of {frameTicks.Length}; a cursor is stepped over one run of durations, and restarted when that run changes.",
                nameof(frameTicks));
        }

        int hold = frameTicks[FrameIndex];
        if (hold <= 0)
        {
            throw new ArgumentException(
                $"frame {FrameIndex} is held for {hold} ticks; every frame is held for at least one fixed step.",
                nameof(frameTicks));
        }

        TicksElapsed++;
        if (TicksElapsed < hold)
        {
            return;
        }

        if (FrameIndex + 1 < frameTicks.Length)
        {
            FrameIndex++;
            TicksElapsed = 0;
            return;
        }

        if (loop)
        {
            FrameIndex = 0;
            TicksElapsed = 0;
            return;
        }

        // The last frame stays current with its ticks spent, so a finished run keeps drawing it.
        IsFinished = true;
    }
}

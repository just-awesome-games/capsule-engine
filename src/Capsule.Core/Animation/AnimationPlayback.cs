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

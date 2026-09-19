namespace Capsule.Animation;

/// <summary>
/// The tick cursor over an ordered run of frames, each held for a whole number of fixed steps. It
/// carries only the position, and what a frame contains belongs to whatever pairs the cursor with a
/// frame table. Each <see cref="Step"/> advances one tick. A looping run wraps to frame 0, and a
/// non-looping run holds its last frame and reports <see cref="IsFinished"/>. This is a mutable
/// value, so call <see cref="Restart"/> when the run it walks changes.
/// </summary>
public struct AnimationPlayback
{
    /// <summary>The frame the cursor is on, from 0.</summary>
    public int FrameIndex { get; private set; }

    /// <summary>
    /// Ticks already spent on <see cref="FrameIndex"/>. Zero on the step the frame became current, and
    /// capped at that frame's duration.
    /// </summary>
    public int TicksElapsed { get; private set; }

    /// <summary>Whether a non-looping run has spent the last frame's ticks. A looping run never finishes.</summary>
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
    /// <paramref name="frameTicks"/> would stand, with tick 0 matching <see cref="Restart"/>. A
    /// looping run wraps the tick modulo the run's total. A non-looping run clamps a tick past its
    /// total to the last frame and finishes.
    /// </summary>
    /// <param name="frameTicks">How many steps each frame is held for, in frame order. Every duration positive.</param>
    /// <param name="loop">Whether the last frame wraps back to frame 0 instead of finishing.</param>
    /// <param name="tick">Ticks elapsed since the run began. Must not be negative.</param>
    /// <exception cref="ArgumentException">The run is empty or holds a non-positive duration.</exception>
    public void Seek(ReadOnlySpan<int> frameTicks, bool loop, int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        if (frameTicks.IsEmpty)
        {
            throw new ArgumentException("Run is empty. Pass at least one frame duration.", nameof(frameTicks));
        }

        // Widened because per-frame int durations can sum past int range.
        long total = 0;
        for (int i = 0; i < frameTicks.Length; i++)
        {
            if (frameTicks[i] <= 0)
            {
                throw new ArgumentException(
                    $"Frame {i} is held for {frameTicks[i]} ticks. Hold every frame for at least one fixed step.",
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
    /// The ticks elapsed since the current pass over <paramref name="frameTicks"/> began. Passing it
    /// back to <see cref="Seek"/> over the same durations reproduces this position.
    /// </summary>
    /// <param name="frameTicks">How many steps each frame is held for, in frame order.</param>
    /// <exception cref="ArgumentException">
    /// The run does not reach the cursor, or its ticks up to the cursor exceed
    /// <see cref="int.MaxValue"/> and <see cref="Seek"/> could not take them back.
    /// </exception>
    public readonly int TickOf(ReadOnlySpan<int> frameTicks)
    {
        if (FrameIndex >= frameTicks.Length)
        {
            throw new ArgumentException(
                $"Cursor is on frame {FrameIndex} of a run of {frameTicks.Length}. Pass the run this cursor walks.",
                nameof(frameTicks));
        }

        // Widened because per-frame int durations can sum past int range.
        long tick = TicksElapsed;
        for (int i = 0; i < FrameIndex; i++)
        {
            tick += frameTicks[i];
        }

        if (tick > int.MaxValue)
        {
            throw new ArgumentException(
                $"Cursor stands {tick} ticks into the run, past {int.MaxValue}. Shorten the run's frame durations.",
                nameof(frameTicks));
        }

        return (int)tick;
    }

    /// <summary>
    /// Advances the cursor by one fixed step over <paramref name="frameTicks"/>. It does nothing once
    /// a non-looping run has finished.
    /// </summary>
    /// <param name="frameTicks">How many steps each frame is held for, with every duration positive. Pass the same run on every step.</param>
    /// <param name="loop">Whether the last frame wraps back to frame 0 instead of finishing.</param>
    /// <exception cref="ArgumentException">The run is empty, does not reach the cursor, or holds a non-positive duration.</exception>
    public void Step(ReadOnlySpan<int> frameTicks, bool loop)
    {
        if (IsFinished)
        {
            return;
        }

        if (FrameIndex >= frameTicks.Length)
        {
            throw new ArgumentException(
                $"Cursor is on frame {FrameIndex} of a run of {frameTicks.Length}. Call Restart when the run changes.",
                nameof(frameTicks));
        }

        int hold = frameTicks[FrameIndex];
        if (hold <= 0)
        {
            throw new ArgumentException(
                $"Frame {FrameIndex} is held for {hold} ticks. Hold every frame for at least one fixed step.",
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

        // The last frame stays current with its ticks spent. A finished run keeps drawing it.
        IsFinished = true;
    }
}

using Capsule.Rendering;

namespace Capsule.Animation;

/// <summary>
/// One animation as sprites: an ordered run of frames, each held for a whole number of fixed steps,
/// played once or on a loop. It is immutable and shareable, so every entity playing a clip reads the
/// same instance and keeps its own <see cref="AnimationPlayback"/> cursor. A clip is identified by
/// instance and has no value equality. Compare the clip playing against the clip a sheet declared.
/// </summary>
public sealed class SpriteClip
{
    private readonly Sprite[] _frames;
    private readonly int[] _frameTicks;

    /// <param name="frames">The frames in play order. At least one.</param>
    /// <param name="frameTicks">How many fixed steps each frame is held for, one per frame and each positive.</param>
    /// <param name="loop">Whether the last frame wraps back to the first instead of finishing.</param>
    /// <exception cref="ArgumentException">There are no frames, the two runs differ in length, or a duration is not positive.</exception>
    public SpriteClip(ReadOnlySpan<Sprite> frames, ReadOnlySpan<int> frameTicks, bool loop = false)
    {
        if (frames.IsEmpty)
        {
            throw new ArgumentException("Expected at least one frame.", nameof(frames));
        }

        if (frames.Length != frameTicks.Length)
        {
            throw new ArgumentException(
                $"{frames.Length} frame(s) came with {frameTicks.Length} duration(s). Pass one duration per frame.",
                nameof(frameTicks));
        }

        for (int i = 0; i < frameTicks.Length; i++)
        {
            if (frameTicks[i] <= 0)
            {
                throw new ArgumentException(
                    $"Frame {i} is held for {frameTicks[i]} ticks. Hold every frame for at least one fixed step.",
                    nameof(frameTicks));
            }
        }

        _frames = frames.ToArray();
        _frameTicks = frameTicks.ToArray();
        Loop = loop;
    }

    /// <summary>Whether the clip wraps from its last frame back to its first.</summary>
    public bool Loop { get; }

    /// <summary>The frames in play order.</summary>
    public ReadOnlySpan<Sprite> Frames => _frames;

    /// <summary>How many fixed steps each frame is held for, in frame order.</summary>
    public ReadOnlySpan<int> FrameTicks => _frameTicks;
}

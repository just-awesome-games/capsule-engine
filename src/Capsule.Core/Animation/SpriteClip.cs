using Capsule.Rendering;

namespace Capsule.Animation;

/// <summary>
/// One named animation as sprites: an ordered run of frames, each held for a whole number of fixed
/// steps, played once or on a loop. Immutable and shareable — every entity playing a clip reads the
/// same instance, and the cursor over it is each entity's own <see cref="AnimationPlayback"/>.
/// Each frame carries its own region and pivot, so frames need not be uniform.
/// <para>
/// A clip is identified by instance and carries no value equality, so the clip playing is compared
/// against the one a sheet declared — <c>animator.Clip == GameSprites.Player.Clips.Run</c>.
/// </para>
/// </summary>
public sealed class SpriteClip
{
    private readonly Sprite[] _frames;
    private readonly int[] _frameTicks;

    /// <param name="frames">The frames in play order; at least one.</param>
    /// <param name="frameTicks">
    /// How many fixed steps each frame is held for, one per frame and every one positive.
    /// </param>
    /// <param name="loop">Whether the last frame wraps back to the first instead of finishing.</param>
    /// <exception cref="ArgumentException">
    /// There are no frames, the two runs differ in length, or a duration is not positive.
    /// </exception>
    public SpriteClip(ReadOnlySpan<Sprite> frames, ReadOnlySpan<int> frameTicks, bool loop = false)
    {
        if (frames.IsEmpty)
        {
            throw new ArgumentException("a clip has at least one frame.", nameof(frames));
        }

        if (frames.Length != frameTicks.Length)
        {
            throw new ArgumentException(
                $"{frames.Length} frame(s) are held for {frameTicks.Length} duration(s); every frame carries its own.",
                nameof(frameTicks));
        }

        for (int i = 0; i < frameTicks.Length; i++)
        {
            if (frameTicks[i] <= 0)
            {
                throw new ArgumentException(
                    $"frame {i} is held for {frameTicks[i]} ticks; every frame is held for at least one fixed step.",
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

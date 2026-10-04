using Capsule.Rendering;

namespace Capsule.Animation;

/// <summary>
/// One animation as sprites: an ordered run of frames, each held for a whole number of fixed steps,
/// played once or on a loop.
/// </summary>
/// <remarks>
/// A clip is immutable. Every entity playing it shares the one instance and keeps its own
/// <see cref="AnimationPlayback"/> cursor. A clip is identified by instance and has no value
/// equality. Compare the clip playing against the clip a sheet declared.
/// </remarks>
public sealed class SpriteClip
{
    private readonly Sprite[] _frames;
    private readonly int[] _frameTicks;

    // Every entry's event names end to end, and where each entry's run starts. Entry i's run ends
    // where entry i + 1's starts, so the starts hold one more element than there are frames. Both
    // are null on a clip with no events.
    private readonly string[]? _events;
    private readonly int[]? _eventStarts;

    /// <summary>Whether the clip wraps from its last frame back to its first.</summary>
    public bool Loop { get; }

    /// <summary>Creates a clip from copies of <paramref name="frames"/>, <paramref name="frameTicks"/> and <paramref name="frameEvents"/>.</summary>
    /// <param name="frames">The frames in play order. At least one.</param>
    /// <param name="frameTicks">How many fixed steps each frame is held for, one per frame and each positive.</param>
    /// <param name="loop">Whether the last frame wraps back to the first instead of finishing.</param>
    /// <param name="frameEvents">
    /// The event names each entry raises as it starts, one array per frame, and none by default. An
    /// empty array is an entry with no events. Names are non-empty and unique within an entry.
    /// </param>
    public SpriteClip(
        ReadOnlySpan<Sprite> frames,
        ReadOnlySpan<int> frameTicks,
        bool loop = false,
        ReadOnlySpan<string[]> frameEvents = default)
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

        AnimationPlayback.ValidatedTotal(frameTicks);

        _frames = frames.ToArray();
        _frameTicks = frameTicks.ToArray();
        Loop = loop;

        if (frameEvents.IsEmpty)
        {
            return;
        }

        if (frameEvents.Length != frames.Length)
        {
            throw new ArgumentException(
                $"{frames.Length} frame(s) came with {frameEvents.Length} event list(s). Pass one list per frame, or none.",
                nameof(frameEvents));
        }

        int count = 0;
        for (int i = 0; i < frameEvents.Length; i++)
        {
            string[] names = frameEvents[i]
                ?? throw new ArgumentException($"Frame {i} has a null event list. Pass an empty array for a frame with no events.", nameof(frameEvents));

            for (int j = 0; j < names.Length; j++)
            {
                if (string.IsNullOrEmpty(names[j]))
                {
                    throw new ArgumentException($"Frame {i} has event {j} with no name. Name every event.", nameof(frameEvents));
                }

                if (Array.IndexOf(names, names[j], 0, j) >= 0)
                {
                    throw new ArgumentException(
                        $"Frame {i} lists event \"{names[j]}\" twice. List each event once per frame.",
                        nameof(frameEvents));
                }
            }

            count += names.Length;
        }

        if (count == 0)
        {
            return;
        }

        _events = new string[count];
        _eventStarts = new int[frames.Length + 1];
        for (int i = 0, at = 0; i < frameEvents.Length; i++)
        {
            _eventStarts[i] = at;
            frameEvents[i].CopyTo(_events, at);
            at += frameEvents[i].Length;
        }

        _eventStarts[frames.Length] = count;
    }

    /// <summary>The frames in play order.</summary>
    public ReadOnlySpan<Sprite> Frames => _frames;

    /// <summary>How many fixed steps each frame is held for, in frame order.</summary>
    public ReadOnlySpan<int> FrameTicks => _frameTicks;

    // The event names the entry at frameIndex raises as it starts, in the order the clip listed them.
    internal ReadOnlySpan<string> EventsAt(int frameIndex) =>
        _eventStarts is null
            ? []
            : _events.AsSpan(_eventStarts[frameIndex], _eventStarts[frameIndex + 1] - _eventStarts[frameIndex]);
}

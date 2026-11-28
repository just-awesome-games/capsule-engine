using System.ComponentModel;
using System.Numerics;
using Capsule.Animation;
using Capsule.Rendering;

namespace Capsule.Assets;

/// <summary>Builds each sprite sheet's frames and clips from the constant data the build wrote. Generated code calls it.</summary>
/// <remarks>A sheet's member holds its data and one call here. Its code then stays the same size however many frames it cuts.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SheetTables
{
    /// <summary>
    /// Each frame's sprite, from its region as x, y, width and height, and its pivot as x and y. Where any frame
    /// flips, <paramref name="flips"/> holds each frame's, 1 for x and 2 for y.
    /// </summary>
    public static Sprite[] Cut(TextureHandle texture, ReadOnlySpan<int> regions, ReadOnlySpan<float> pivots, ReadOnlySpan<int> flips)
    {
        Sprite[] frames = new Sprite[pivots.Length / 2];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = new Sprite(
                texture,
                new TextureRegion(regions[i * 4], regions[(i * 4) + 1], regions[(i * 4) + 2], regions[(i * 4) + 3]),
                new Vector2(pivots[i * 2], pivots[(i * 2) + 1]),
                FlipX: !flips.IsEmpty && (flips[i] & 1) != 0,
                FlipY: !flips.IsEmpty && (flips[i] & 2) != 0);
        }

        return frames;
    }

    /// <summary>
    /// Sets each marked frame's marks. Per marked frame, <paramref name="marks"/> holds its index, its socket and box
    /// counts and each one's name index, and <paramref name="points"/> each socket's x and y, then each box's x, y,
    /// width and height.
    /// </summary>
    public static Sprite[] Mark(Sprite[] frames, ReadOnlySpan<string> sockets, ReadOnlySpan<string> boxes, ReadOnlySpan<int> marks, ReadOnlySpan<float> points)
    {
        ArgumentNullException.ThrowIfNull(frames);

        for (int at = 0, point = 0; at < marks.Length;)
        {
            int frame = marks[at];
            SpriteSocket[] set = new SpriteSocket[marks[at + 1]];
            SpriteBox[] boxed = new SpriteBox[marks[at + 2]];
            at += 3;
            for (int i = 0; i < set.Length; i++, point += 2)
            {
                set[i] = new SpriteSocket(sockets[marks[at++]], new Vector2(points[point], points[point + 1]));
            }

            for (int i = 0; i < boxed.Length; i++, point += 4)
            {
                boxed[i] = new SpriteBox(boxes[marks[at++]], new Rect(points[point], points[point + 1], points[point + 2], points[point + 3]));
            }

            frames[frame] = frames[frame] with { Marks = new SpriteMarks(set, boxed) };
        }

        return frames;
    }

    /// <summary>
    /// Each clip, over the entries up to its end: each entry's frame index and ticks, and, where
    /// <paramref name="events"/> holds any, its event count and each one's name index.
    /// </summary>
    public static SpriteClip[] Clips(
        ReadOnlySpan<Sprite> frames,
        ReadOnlySpan<int> ends,
        ReadOnlySpan<int> picks,
        ReadOnlySpan<int> ticks,
        ReadOnlySpan<bool> loops,
        ReadOnlySpan<string> names,
        ReadOnlySpan<int> events)
    {
        SpriteClip[] clips = new SpriteClip[ends.Length];
        for (int i = 0, start = 0, at = 0; i < clips.Length; start = ends[i], i++)
        {
            Sprite[] played = new Sprite[ends[i] - start];
            string[][] raised = events.IsEmpty ? [] : new string[played.Length][];
            for (int entry = 0; entry < played.Length; entry++)
            {
                played[entry] = frames[picks[start + entry]];
                if (!events.IsEmpty)
                {
                    raised[entry] = new string[events[at++]];
                    for (int name = 0; name < raised[entry].Length; name++)
                    {
                        raised[entry][name] = names[events[at++]];
                    }
                }
            }

            clips[i] = new SpriteClip(played, ticks[start..ends[i]], loops[i], raised);
        }

        return clips;
    }

    /// <summary>The frames at <paramref name="picks"/>, in order, a negative pick drawing nothing.</summary>
    public static Sprite?[] Drawn(ReadOnlySpan<Sprite> frames, ReadOnlySpan<int> picks)
    {
        Sprite?[] drawn = new Sprite?[picks.Length];
        for (int i = 0; i < picks.Length; i++)
        {
            drawn[i] = picks[i] < 0 ? null : frames[picks[i]];
        }

        return drawn;
    }
}

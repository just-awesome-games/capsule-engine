using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Rendering;

namespace Capsule.Diagnostics;

// One straight segment a DebugDraw call decomposed to. A null Color is the channel's, resolved
// when drawn. Motion is how far what the draw follows moved this step, so the host can place it
// where that thing is drawn between steps. Kept until the settled tick passes ExpiresAtTick.
internal readonly record struct DebugDrawSegment(
    string Channel,
    Vector2 A,
    Vector2 B,
    ColorRgba? Color,
    long ExpiresAtTick,
    Vector2 Motion);

// One label, its top-left corner at Position; Color, Motion and expiry as a segment's.
internal readonly record struct DebugDrawLabel(
    string Channel,
    Vector2 Position,
    string Text,
    ColorRgba? Color,
    long ExpiresAtTick,
    Vector2 Motion);

// Where DebugDraw calls land once a host attaches it: the live segments and labels, the channels
// that have emitted so far, and the tick a new draw expires against. The host settles it once per
// frame at the scheduler's tick, which prunes what has expired and stamps what the next step
// emits. Single-threaded, like the simulation that writes it.
internal sealed class DebugDrawBuffer
{
    private readonly List<DebugDrawSegment> _segments = [];
    private readonly List<DebugDrawLabel> _labels = [];
    private readonly HashSet<string> _channels = new(StringComparer.Ordinal);
    private long _tick;

    internal ReadOnlySpan<DebugDrawSegment> Segments => CollectionsMarshal.AsSpan(_segments);

    internal ReadOnlySpan<DebugDrawLabel> Labels => CollectionsMarshal.AsSpan(_labels);

    // Every channel that has emitted since the buffer was created, in no order.
    internal IReadOnlyCollection<string> Channels => _channels;

    internal void Segment(string channel, Vector2 a, Vector2 b, ColorRgba? color, int steps, Vector2 motion)
    {
        _channels.Add(channel);
        _segments.Add(new DebugDrawSegment(channel, a, b, color, ExpiryFor(steps), motion));
    }

    internal void Label(string channel, Vector2 position, string text, ColorRgba? color, int steps, Vector2 motion)
    {
        _channels.Add(channel);
        _labels.Add(new DebugDrawLabel(channel, position, text, color, ExpiryFor(steps), motion));
    }

    // tick is the scheduler's settled tick: the one the next step will run. A draw emitted during
    // step N for one step expires at N + 1, so it is shown on the frame settled after step N and
    // gone once step N + 1 has run.
    internal void Settle(long tick)
    {
        _tick = tick;

        // Compacted in place, so the lists keep their capacity and a warm buffer allocates nothing.
        Span<DebugDrawSegment> segments = CollectionsMarshal.AsSpan(_segments);
        int keptSegments = 0;
        for (int index = 0; index < segments.Length; index++)
        {
            if (segments[index].ExpiresAtTick >= tick)
            {
                segments[keptSegments++] = segments[index];
            }
        }

        _segments.RemoveRange(keptSegments, segments.Length - keptSegments);

        Span<DebugDrawLabel> labels = CollectionsMarshal.AsSpan(_labels);
        int keptLabels = 0;
        for (int index = 0; index < labels.Length; index++)
        {
            if (labels[index].ExpiresAtTick >= tick)
            {
                labels[keptLabels++] = labels[index];
            }
        }

        _labels.RemoveRange(keptLabels, labels.Length - keptLabels);
    }

    private long ExpiryFor(int steps) => _tick + Math.Max(steps, 1);
}

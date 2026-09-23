using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// Reads the attached debug draw buffer onto the overlay's world list: every live segment and label
// whose channel is switched on, each drawn where the frame draws what it follows and in its own colour
// or its channel's. The overlay's single world entity holds it, so it draws under the menu and over the
// game. Allocation-free once the frame's lists have grown.
internal sealed class DebugDrawRenderer : Renderer
{
    private readonly DebugDrawBuffer _buffer;
    private readonly IReadOnlySet<string> _enabled;

    internal DebugDrawRenderer(DebugDrawBuffer buffer, IReadOnlySet<string> enabled)
    {
        _buffer = buffer;
        _enabled = enabled;
    }

    // World units per font pixel, which keeps a label's glyphs screen-sized. It is the overlay's
    // integer scale over the back buffer's pixels per world unit. One until a frame has been drawn.
    internal float TextScale { get; set; } = 1f;

    // The fraction of a step the frame is drawn at, matching the game frame. A draw that follows
    // something interpolating is moved back along its motion by what is not yet simulated, so a
    // collider stays on its sprite. One for a settled frame, as a held run always is.
    internal float Alpha { get; set; } = 1f;

    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        float unsimulated = 1f - Alpha;

        foreach (ref readonly DebugDrawSegment segment in _buffer.Segments)
        {
            if (_enabled.Contains(segment.Channel))
            {
                Vector2 back = segment.Motion * unsimulated;
                view.Add(new LineIntent(segment.A - back, segment.B - back, segment.Color ?? DebugDraw.ColorOf(segment.Channel)));
            }
        }

        foreach (ref readonly DebugDrawLabel label in _buffer.Labels)
        {
            if (_enabled.Contains(label.Channel))
            {
                Vector2 position = label.Position - (label.Motion * unsimulated);
                view.Add(new TextIntent(
                    BitmapFont.Default,
                    label.Text,
                    position,
                    position,
                    new Vector2(TextScale),
                    label.Color ?? DebugDraw.ColorOf(label.Channel)));
            }
        }
    }
}

// The overlay's world entity, at the origin so the reader's positions are the buffer's own.
internal sealed class DebugDrawEntity : Entity
{
    internal DebugDrawEntity(DebugDrawRenderer renderer)
        : base(Vector2.Zero) => Add(renderer);
}

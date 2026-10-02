using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// Draws the enabled debug-draw channels under the menu and over the game. Allocation-free once warm.
internal sealed class DebugDrawRenderer : Renderer
{
    private readonly DebugDrawBuffer _buffer = new();
    private readonly HashSet<string> _enabled = new(StringComparer.Ordinal);
    private readonly FixedStepScheduler _scheduler;
    private readonly SceneHost _scenes;

    // World units per font pixel, which keeps label glyphs screen-sized. One until a frame has drawn.
    internal float TextScale { get; set; } = 1f;

    // The frame's interpolation alpha. A draw moves back by its unsimulated motion onto its sprite.
    internal float Alpha { get; set; } = 1f;

    internal DebugDrawRenderer(FixedStepScheduler scheduler, SceneHost scenes)
    {
        _scheduler = scheduler;
        _scenes = scenes;
    }

    internal override bool Steps => false;

    internal bool AnyEnabled => _enabled.Count > 0;

    // Channels named since the buffer was attached, in reading order.
    internal string[] Channels
    {
        get
        {
            string[] channels = [.. _buffer.Channels];
            Array.Sort(channels, OverlayRow.CompareLabels);

            return channels;
        }
    }

    internal bool IsEnabled(string channel) => _enabled.Contains(channel);

    // The held scene emits at once. The toggle then shows without a step.
    internal void Toggle(string channel)
    {
        if (!_enabled.Remove(channel))
        {
            _enabled.Add(channel);
        }

        Attach();
        Emit();
    }

    // Attached only while the run is held or a channel is on. Only the overlay sets the hold.
    internal void Attach() => DebugDraw.UseBuffer(_scheduler.Held || AnyEnabled ? _buffer : null);

    // Settles once at the frame's last tick. A multi-step frame's later draws leave early.
    internal void Settle() => _buffer.Settle(_scheduler.Tick);

    // Runs the held scene's debug pass unless the settled step drew. Its draws expire next step.
    internal void Emit()
    {
        long tick = _scheduler.Tick;
        if (!DebugDraw.IsAttached || _buffer.EmittedTick >= tick - 1)
        {
            return;
        }

        _buffer.Settle(tick - 1);
        _scenes.EmitDebugDraws();
        Settle();
    }

    protected internal override void Draw(FrameView view)
    {
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
                    OverlayScene.Font,
                    label.Text,
                    position,
                    position,
                    new Vector2(TextScale),
                    label.Color ?? DebugDraw.ColorOf(label.Channel)));
            }
        }
    }
}

// At the origin, which makes the buffer's positions world positions.
internal sealed class DebugDrawEntity : Entity
{
    internal DebugDrawEntity(DebugDrawRenderer renderer)
        : base(Vector2.Zero) => Add(renderer);
}

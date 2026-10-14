using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// Draws the enabled debug-draw channels under the menu and over the game. Allocation-free once warm.
internal sealed class DebugDrawRenderer : Renderer
{
    // The outline colours of the selected entity's own renderers and of its descendants' renderers.
    internal static readonly ColorRgba SelectionColor = ColorRgba.Orange;
    internal static readonly ColorRgba ChildSelectionColor = ColorRgba.DodgerBlue;

    private readonly DebugDrawBuffer _buffer = new();
    private readonly HashSet<string> _enabled = new(StringComparer.Ordinal);
    private readonly FixedStepScheduler _scheduler;
    private readonly SceneHost _scenes;

    // The last owner asked about and the answer. A run of draws shares an owner.
    private object? _lastOwner;
    private bool _lastOwned;

    // World units per font pixel, which keeps label glyphs screen-sized. One until a frame has drawn.
    internal float TextScale { get; set; } = 1f;

    // The frame's interpolation alpha. A draw moves back by its unsimulated motion onto its sprite.
    internal float Alpha { get; set; } = 1f;

    // The entity the overlay highlights, or null. Its subtree's draws show whatever the toggles say. They draw
    // after everything else, and its renderers' bounds are outlined.
    internal Entity? Selected { get; set; }

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
        _lastOwner = null;
        _lastOwned = false;

        // The selection's draws belong to the second pass. They land on top there.
        foreach (ref readonly DebugDrawSegment segment in _buffer.Segments)
        {
            if (_enabled.Contains(segment.Channel) && !InSelection(segment.Owner))
            {
                AddSegment(view, in segment, unsimulated);
            }
        }

        foreach (ref readonly DebugDrawLabel label in _buffer.Labels)
        {
            if (_enabled.Contains(label.Channel) && !InSelection(label.Owner))
            {
                AddLabel(view, in label, unsimulated);
            }
        }

        if (Selected is not { } selected)
        {
            return;
        }

        foreach (ref readonly DebugDrawSegment segment in _buffer.Segments)
        {
            if (InSelection(segment.Owner))
            {
                AddSegment(view, in segment, unsimulated);
            }
        }

        foreach (ref readonly DebugDrawLabel label in _buffer.Labels)
        {
            if (InSelection(label.Owner))
            {
                AddLabel(view, in label, unsimulated);
            }
        }

        Outline(view, selected, SelectionColor, unsimulated);
    }

    private static void AddSegment(FrameView view, in DebugDrawSegment segment, float unsimulated)
    {
        Vector2 back = segment.Motion * unsimulated;
        view.Add(new LineIntent(segment.A - back, segment.B - back, segment.Color ?? DebugDraw.ColorOf(segment.Channel)));
    }

    private void AddLabel(FrameView view, in DebugDrawLabel label, float unsimulated)
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

    // Whether the draw's owner is the selected entity or sits under it.
    private bool InSelection(object? owner)
    {
        if (Selected is not { } selected || owner is not Entity entity)
        {
            return false;
        }

        if (!ReferenceEquals(owner, _lastOwner))
        {
            _lastOwner = owner;
            _lastOwned = false;
            for (Entity? up = entity; up is not null && !_lastOwned; up = up.Parent)
            {
                _lastOwned = ReferenceEquals(up, selected);
            }
        }

        return _lastOwned;
    }

    // Outlines each renderer's bounds in the subtree where the sprite is drawn at the frame's alpha. The bounds
    // describe the entity's current transform. The outline moves back by the motion the sprite has yet to
    // make, as a draw does. Descendants take the child colour.
    private static void Outline(FrameView view, Entity entity, ColorRgba color, float unsimulated)
    {
        if (entity.Space == RenderSpace.World)
        {
            Vector2 back = (entity.World.Position - entity.PreviousWorld.Position) * unsimulated;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer { Bounds: { IsEmpty: false } bounds })
                {
                    Vector2 topLeft = new Vector2(bounds.Left, bounds.Top) - back;
                    Vector2 topRight = new Vector2(bounds.Right, bounds.Top) - back;
                    Vector2 bottomRight = new Vector2(bounds.Right, bounds.Bottom) - back;
                    Vector2 bottomLeft = new Vector2(bounds.Left, bounds.Bottom) - back;
                    view.Add(new LineIntent(topLeft, topRight, color));
                    view.Add(new LineIntent(topRight, bottomRight, color));
                    view.Add(new LineIntent(bottomRight, bottomLeft, color));
                    view.Add(new LineIntent(bottomLeft, topLeft, color));
                }
            }
        }

        foreach (Entity child in entity.Children)
        {
            Outline(view, child, ChildSelectionColor, unsimulated);
        }
    }
}

// At the origin, which makes the buffer's positions world positions.
internal sealed class DebugDrawEntity : Entity
{
    internal DebugDrawEntity(DebugDrawRenderer renderer)
        : base(Vector2.Zero) => Add(renderer);
}

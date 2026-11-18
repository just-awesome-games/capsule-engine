using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools.Overlay;

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

    // Drops the entities it holds of a scene the run has left. The overlay's next highlight selects again.
    internal void ClearSelection()
    {
        Selected = null;
        _lastOwner = null;
    }

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

    // Each draw carries its motion over the step, and the renderer draws it where its sprite is drawn between
    // the step's two ends.
    protected internal override void Draw(FrameView view)
    {
        _lastOwner = null;
        _lastOwned = false;

        // The selection's draws belong to the second pass. They land on top there.
        foreach (ref readonly DebugDrawSegment segment in _buffer.Segments)
        {
            if (_enabled.Contains(segment.Channel) && !InSelection(segment.Owner))
            {
                AddSegment(view, in segment);
            }
        }

        foreach (ref readonly DebugDrawLabel label in _buffer.Labels)
        {
            if (_enabled.Contains(label.Channel) && !InSelection(label.Owner))
            {
                AddLabel(view, in label);
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
                AddSegment(view, in segment);
            }
        }

        foreach (ref readonly DebugDrawLabel label in _buffer.Labels)
        {
            if (InSelection(label.Owner))
            {
                AddLabel(view, in label);
            }
        }

        Outline(view, selected, SelectionColor);
    }

    private static void AddSegment(FrameView view, in DebugDrawSegment segment) =>
        view.Add(new LineIntent(segment.A, segment.B, segment.Color ?? DebugDraw.ColorOf(segment.Channel)) { Motion = segment.Motion });

    private void AddLabel(FrameView view, in DebugDrawLabel label)
    {
        view.Add(new TextIntent(
            OverlayScene.Font,
            label.Text,
            label.Position - label.Motion,
            label.Position,
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

    // Outlines each renderer's bounds in the subtree, moving with the entity over the step as its sprite does.
    // The bounds describe the entity's current transform. Descendants take the child colour.
    private static void Outline(FrameView view, Entity entity, ColorRgba color)
    {
        if (entity.Space == RenderSpace.World)
        {
            Vector2 motion = entity.World.Position - entity.PreviousWorld.Position;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer { Bounds: { IsEmpty: false } bounds })
                {
                    Vector2 topLeft = new(bounds.Left, bounds.Top);
                    Vector2 topRight = new(bounds.Right, bounds.Top);
                    Vector2 bottomRight = new(bounds.Right, bounds.Bottom);
                    Vector2 bottomLeft = new(bounds.Left, bounds.Bottom);
                    Edge(topLeft, topRight);
                    Edge(topRight, bottomRight);
                    Edge(bottomRight, bottomLeft);
                    Edge(bottomLeft, topLeft);
                }
            }

            void Edge(Vector2 a, Vector2 b) => view.Add(new LineIntent(a, b, color) { Motion = motion });
        }

        foreach (Entity child in entity.Children)
        {
            Outline(view, child, ChildSelectionColor);
        }
    }
}

// At the origin, which makes the buffer's positions world positions.
internal sealed class DebugDrawEntity : Entity
{
    internal DebugDrawEntity(DebugDrawRenderer renderer)
        : base(Vector2.Zero) => Add(renderer);
}

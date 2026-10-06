using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Watches a rect on its entity against the scene camera's <see cref="Camera.VisibleRegion"/> and
/// reports when it comes on screen and when it leaves. The rect is corner-anchored like a
/// <see cref="Physics.BoxCollider2D"/>: its corner sits at the entity's position plus
/// <see cref="Offset"/> and it spans <see cref="Size"/> from there.
/// </summary>
/// <remarks>
/// This is simulation state, settled once per step after the step's deferred adds land. From its
/// entity's first step, <see cref="IsOnScreen"/> and the events describe that step's frame. Sharing
/// an edge with the region is not being on screen.
/// <para>
/// The rect follows scale in the entity's ancestry as a box collider does. <see cref="Offset"/> and
/// <see cref="Size"/> are multiplied by the entity's world scale, and a negative axis mirrors the rect
/// about the entity's position. A rect scaled to no extent on an axis is off screen. Rotation in the
/// ancestry and an <see cref="Entity.ScrollFactor"/> other than one are refused while the notifier is
/// present.
/// </para>
/// </remarks>
/// <example>
/// A shot faces left by mirroring its whole entity, and the notifier mirrors with it:
/// <code>
/// Add(new VisibleOnScreenNotifier2D(new Vector2(12f, 8f)) { Offset = new Vector2(2f, -4f) });
/// Scale = new Vector2(-1f, 1f);
/// </code>
/// </example>
public sealed class VisibleOnScreenNotifier2D : Component
{
    private Vector2 _size;
    private Vector2 _offset;
    private Scene? _scene;
    private bool _dispatching;

    /// <param name="size">The extent the rect spans from its corner, in the entity's own units.</param>
    public VisibleOnScreenNotifier2D(Vector2 size) => _size = RequireSize(size);

    /// <summary>
    /// Raised from the settle that first finds the rect overlapping the visible region. It is never raised
    /// twice without a <see cref="ScreenExited"/> in between.
    /// </summary>
    public event Action? ScreenEntered;

    /// <summary>Raised when the rect stops overlapping the visible region.</summary>
    /// <remarks>
    /// A notifier on screen when it leaves its scene or is detached raises it once more. Every
    /// enter is paired with one exit.
    /// </remarks>
    public event Action? ScreenExited;

    /// <summary>The extent the rect spans from its corner, in the entity's own units.</summary>
    /// <exception cref="InvalidOperationException">Set from inside this notifier's own handler.</exception>
    public Vector2 Size
    {
        get => _size;
        set
        {
            RequireNotDispatching();
            _size = RequireSize(value);
        }
    }

    /// <summary>Added to the entity's position to place the rect's corner, in the entity's own units. Zero by default.</summary>
    /// <exception cref="InvalidOperationException">Set from inside this notifier's own handler.</exception>
    public Vector2 Offset
    {
        get => _offset;
        set
        {
            RequireNotDispatching();
            Guard.Finite(value, nameof(value));
            _offset = value;
        }
    }

    /// <summary>
    /// Whether the rect overlapped the camera's visible region at the last settle. Reads false before the
    /// first settle and while the notifier is outside a scene.
    /// </summary>
    public bool IsOnScreen { get; private set; }

    internal override bool Steps => false;

    internal override TransformSupport Supports => TransformSupport.Resize;

    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        _scene = Entity!.Scene;
        _scene.TrackVisibility(this);
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        _scene?.UntrackVisibility(this);
        _scene = null;

        // Clear the flag before the handler runs. A handler that reads the notifier sees it off screen,
        // and no second exit is owed.
        if (IsOnScreen)
        {
            IsOnScreen = false;
            Raise(ScreenExited);
        }
    }

    // Tests the full rect against the full region. A partial overlap counts as on screen, matching
    // how the renderer shows a partly framed sprite.
    internal void SettleVisibility(in Rect region)
    {
        Rect rect = WorldRect(Entity!.World);
        bool onScreen = !region.IsEmpty && !rect.IsEmpty && region.Intersects(rect);

        if (onScreen == IsOnScreen)
        {
            return;
        }

        IsOnScreen = onScreen;
        Raise(onScreen ? ScreenEntered : ScreenExited);
    }

    // Scales both corners about the entity's position. A scale of one keeps the plain translation, which
    // leaves an unscaled rect exactly as it was.
    private Rect WorldRect(in Transform2D world)
    {
        if (world.Scale == Vector2.One)
        {
            return new Rect(world.Position + _offset, _size);
        }

        Vector2 first = world.Position + (_offset * world.Scale);
        Vector2 second = world.Position + ((_offset + _size) * world.Scale);
        Vector2 min = Vector2.Min(first, second);
        Vector2 max = Vector2.Max(first, second);

        return new Rect(min.X, min.Y, max.X, max.Y);
    }

    // A handler sees the new state and may not resize or move the notifier it is running for.
    private void Raise(Action? handler)
    {
        _dispatching = true;
        try
        {
            handler?.Invoke();
        }
        finally
        {
            _dispatching = false;
        }
    }

    private void RequireNotDispatching()
    {
        if (_dispatching)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} cannot change from inside its own handler. Change it after the handler returns.");
        }
    }

    private static Vector2 RequireSize(Vector2 size)
    {
        Guard.Positive(size.X, nameof(size));
        Guard.Positive(size.Y, nameof(size));

        return size;
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("IsOnScreen", IsOnScreen);
        panel.Field("Size", Size);
        panel.Field("Offset", Offset);
    }
}

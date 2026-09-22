using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Watches a rect on its entity against the scene camera's <see cref="Camera.VisibleRegion"/> and reports
/// when it comes on screen and when it leaves. The rect is corner-anchored like a
/// <see cref="Physics.BoxCollider2D"/>: its corner sits at the entity's position plus <see cref="Offset"/>
/// and it spans <see cref="Size"/> world units from there.
/// <para>
/// This is simulation state, settled once per step after the step's deferred adds land, so from its
/// entity's first step <see cref="IsOnScreen"/> and the events describe that step's frame. Sharing an
/// edge with the region is not being on screen. An entity whose <see cref="Entity.ScrollFactor"/> is
/// not one rejects this component: it would draw somewhere other than the rect this measures.
/// </para>
/// </summary>
public sealed class VisibleOnScreenNotifier2D : Component
{
    private Vector2 _size;
    private Vector2 _offset;
    private Scene? _scene;
    private bool _dispatching;

    /// <param name="size">The extent the rect spans from its corner, in world units.</param>
    public VisibleOnScreenNotifier2D(Vector2 size) => _size = RequireSize(size);

    /// <summary>
    /// Raised from the settle that first finds the rect overlapping the visible region. It is never raised
    /// twice without a <see cref="ScreenExited"/> in between.
    /// </summary>
    public event Action? ScreenEntered;

    /// <summary>
    /// Raised when the rect stops overlapping the visible region, and once for a notifier that was on screen
    /// when it left its scene or was detached from its entity, which keeps every enter paired with one exit.
    /// </summary>
    public event Action? ScreenExited;

    /// <summary>The extent the rect spans from its corner, in world units.</summary>
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

    /// <summary>Added to the entity's position to place the rect's corner. Zero by default.</summary>
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

    internal override TransformSupport Supports => TransformSupport.Position;

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
        bool onScreen = !region.IsEmpty &&
            region.Intersects(new Rect(Entity!.WorldPosition + _offset, _size));

        if (onScreen == IsOnScreen)
        {
            return;
        }

        IsOnScreen = onScreen;
        Raise(onScreen ? ScreenEntered : ScreenExited);
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

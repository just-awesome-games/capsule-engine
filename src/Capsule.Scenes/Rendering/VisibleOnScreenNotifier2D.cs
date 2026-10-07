using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Watches a <see cref="Rendering.Rect"/> on its entity against the scene camera's
/// <see cref="Camera.VisibleRegion"/>, grown by <see cref="Margin"/>, and reports when it comes on screen and when it
/// leaves.
/// </summary>
/// <remarks>
/// <see cref="Rect"/> is in the entity's own units about its position, corner-anchored like a
/// <see cref="Physics.BoxCollider2D"/>. The default rect has no extent and sits at the origin. A plain notifier
/// watches its entity's position as a point. An axis with no extent is tested as a line. A line is on screen only
/// strictly inside the region. Sharing an edge with the region is not being on screen.
/// <para>
/// This is simulation state, settled once per step after the step's deferred adds land. A notifier that joins
/// between steps settles as the next step begins, against the last frame, before any entity steps. From its entity's first step,
/// <see cref="IsOnScreen"/> and the events describe that step's frame. While the camera's region is empty,
/// nothing is on screen, whatever the margin.
/// </para>
/// <para>
/// The rect follows scale in the entity's ancestry as a box collider does. Its corner and size are multiplied by the
/// entity's world scale, and a negative axis mirrors the rect about the entity's position. <see cref="Margin"/> is in
/// world units and never scales. Rotation in the ancestry and an <see cref="Entity.ScrollFactor"/> other than one are
/// refused while the notifier is present.
/// </para>
/// </remarks>
/// <example>
/// A shot watches its body and leaves once all of it is off screen. A spawner watches its own position and fires
/// 32 units before that position reaches the view:
/// <code>
/// Add(new VisibleOnScreenNotifier2D { Rect = new Rect(new Vector2(2f, -4f), new Vector2(12f, 8f)) });
/// Add(new VisibleOnScreenNotifier2D { Margin = new Vector2(32f) });
/// </code>
/// </example>
public sealed class VisibleOnScreenNotifier2D : Component
{
    private Rect _rect;
    private Vector2 _margin;
    private Scene? _scene;
    private bool _dispatching;

    /// <summary>
    /// Raised from the settle that first finds the rect overlapping the grown region. It is never raised twice
    /// without a <see cref="ScreenExited"/> in between.
    /// </summary>
    public event Action? ScreenEntered;

    /// <summary>Raised when the rect stops overlapping the grown region.</summary>
    /// <remarks>
    /// A notifier on screen when it leaves its scene or is detached raises it once more. Every
    /// enter is paired with one exit.
    /// </remarks>
    public event Action? ScreenExited;

    /// <summary>The rect it watches in the entity's own units, a point at the entity's position by default.</summary>
    /// <exception cref="InvalidOperationException">Set from inside this notifier's own handler.</exception>
    public Rect Rect
    {
        get => _rect;
        set
        {
            RequireNotDispatching();
            Guard.Finite(value.Position, nameof(value));
            Guard.NonNegative(value.Size, nameof(value));
            _rect = value;
        }
    }

    /// <summary>
    /// How far the visible region is grown before the rect is tested against it, in world units: X on the left and
    /// right, Y above and below. Zero, the default, tests against the region itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">Set from inside this notifier's own handler.</exception>
    public Vector2 Margin
    {
        get => _margin;
        set
        {
            RequireNotDispatching();
            Guard.NonNegative(value, nameof(value));
            _margin = value;
        }
    }

    /// <summary>
    /// Whether the rect overlapped the grown region at the last settle. Reads false before the first settle and while
    /// the notifier is outside a scene.
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

    // A partial overlap counts as on screen, matching how the renderer shows a partly framed sprite. The open
    // overlap test also takes an axis with no extent as a line strictly inside the region.
    internal void SettleVisibility(in Rect region)
    {
        Rect grown = new(region.Left - _margin.X, region.Top - _margin.Y, region.Right + _margin.X, region.Bottom + _margin.Y);
        bool onScreen = !region.IsEmpty && grown.Intersects(WorldRect(Entity!.World));

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
            return new Rect(world.Position + _rect.Position, _rect.Size);
        }

        Vector2 first = world.Position + (_rect.Position * world.Scale);
        Vector2 second = world.Position + ((_rect.Position + _rect.Size) * world.Scale);
        Vector2 min = Vector2.Min(first, second);
        Vector2 max = Vector2.Max(first, second);

        return new Rect(min.X, min.Y, max.X, max.Y);
    }

    // A handler sees the new state and may not change the notifier it is running for.
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

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("IsOnScreen", IsOnScreen);
        panel.Field("Rect", Rect);
        panel.Field("Margin", Margin);
    }
}

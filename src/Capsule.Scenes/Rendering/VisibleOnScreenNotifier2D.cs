using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes.Rendering;

/// <summary>
/// Watches a rect on its entity against the scene camera's <see cref="Camera.VisibleRegion"/> and
/// says when it comes on screen and when it leaves. Corner-anchored like a
/// <see cref="Physics.BoxCollider2D"/>: the rect's corner is the entity's position plus
/// <see cref="Offset"/>, and it spans <see cref="Size"/> world units from there.
/// <para>
/// Simulation only — nothing here reads the renderer, the window or the output, so a headless run
/// answers exactly as a windowed one does. It settles once a step, immediately after the camera's
/// late step, and a notifier that arrives with that step's deferred adds settles as they land,
/// against the region that step settled — so from its entity's first step
/// <see cref="IsOnScreen"/> and the events describe the frame last drawn, including the frame its
/// own spawn step drew. Sharing an edge with the visible region is not being on it, and a region
/// spanning nothing puts everything off screen. A notifier added to a scene from inside another
/// notifier's handler first settles on the next step.
/// </para>
/// </summary>
public sealed class VisibleOnScreenNotifier2D : Component
{
    private Vector2 _size;
    private Vector2 _offset;
    private Scene? _scene;

    /// <param name="size">The extent the rect spans from its corner, in world units.</param>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is not positive and finite.</exception>
    public VisibleOnScreenNotifier2D(Vector2 size) => _size = RequireSize(size);

    /// <summary>
    /// Raised as the rect begins overlapping the visible region, from the settle that found it
    /// there. Never raised twice without a <see cref="ScreenExited"/> between.
    /// </summary>
    public event Action? ScreenEntered;

    /// <summary>
    /// Raised as the rect stops overlapping the visible region, and once for a notifier that was on
    /// screen when it left its scene or was detached from its entity, so the pairing stays exact.
    /// </summary>
    public event Action? ScreenExited;

    /// <summary>The extent the rect spans from its corner, in world units.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A component of the size is not positive and finite.</exception>
    public Vector2 Size
    {
        get => _size;
        set => _size = RequireSize(value);
    }

    /// <summary>Added to the entity's position to place the rect's corner; zero by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The offset is not finite.</exception>
    public Vector2 Offset
    {
        get => _offset;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A notifier's offset must be finite.");
            }

            _offset = value;
        }
    }

    /// <summary>
    /// Whether the rect overlapped the visible region of the last frame drawn, from its entity's
    /// first step onwards. False for a notifier in no scene, and until the first settle after its
    /// scene's camera has framed a step.
    /// </summary>
    public bool IsOnScreen { get; private set; }

    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        _scene = Entity!.Scene!;
        _scene.TrackVisibility(this);
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        _scene?.UntrackVisibility(this);
        _scene = null;

        // Cleared before the handler runs, so one that reads the notifier from in here sees it off
        // screen rather than owing a second exit.
        if (IsOnScreen)
        {
            IsOnScreen = false;
            ScreenExited?.Invoke();
        }
    }

    // The whole rect against the whole region: a partial overlap is on screen, exactly as the
    // renderer would show a partly framed sprite.
    internal void SettleVisibility(in Rect region)
    {
        bool onScreen = !region.IsEmpty &&
            region.Intersects(new Rect(Entity!.Position + _offset, _size));

        if (onScreen == IsOnScreen)
        {
            return;
        }

        IsOnScreen = onScreen;
        if (onScreen)
        {
            ScreenEntered?.Invoke();
        }
        else
        {
            ScreenExited?.Invoke();
        }
    }

    private static Vector2 RequireSize(Vector2 size)
    {
        // Negated so a NaN extent is rejected alongside the non-positive ones.
        if (!(size.X > 0f) || !(size.Y > 0f) || !float.IsFinite(size.X) || !float.IsFinite(size.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A notifier's size must be positive and finite on both axes.");
        }

        return size;
    }
}

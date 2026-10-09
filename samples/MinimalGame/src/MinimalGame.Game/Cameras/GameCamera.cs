using System.Numerics;
using Capsule.Animation;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using MinimalGame.Game.Entities;

namespace MinimalGame.Game.Cameras;

/// <summary>
/// The follow camera: it owns the span the room is framed at and the feel of the follow, confines the
/// view to the room, and follows the player. While the player's probe stands in a <see cref="CameraZone"/>
/// the view is confined to that zone instead, sliding in on a set curve and easing back out on the
/// camera's own chase once the probe leaves every zone. A scene installs it and touches it no further.
/// </summary>
public sealed class GameCamera : Camera
{
    private static readonly BoundsTransition SlideIn = BoundsTransition.Eased(0.5f, Ease.InOutSine);

    private Collider2D _probe = null!;
    private Rect? _outside;
    private bool _inZone;

    public GameCamera()
    {
        ViewportSize = World.ViewportSize;

        // The box the aim moves in without moving the camera. Its height holds the camera through an
        // ordinary 40 px jump. Widen it for a calmer camera, shrink it towards zero for a tighter one.
        Deadzone = new Vector2(16f, 96f);

        // Seconds the camera takes to catch its aim. Lower snaps, higher drifts.
        SmoothTime = 0.25f;

        // Seconds the view takes to settle back out of a camera zone's bounds. Lower snaps, higher drifts.
        BoundsTransition = BoundsTransition.Smooth(0.35f);
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        // A scene with no tile map spans nothing, and bounds of no size would pin the view.
        if (Scene.Size.X > 0f && Scene.Size.Y > 0f)
        {
            Bounds = new Rect(Vector2.Zero, Scene.Size);
        }

        Player player = Scene.FindSingle<Player>();
        Follow(player);

        _probe = player.Probe;
        _probe.ContactEntered += OnProbeEntered;
        _probe.ContactExited += OnProbeExited;
    }

    // Stepping in from a neighbouring zone keeps the bounds held before the first zone.
    private void OnProbeEntered(ColliderContact2D contact)
    {
        if (contact.OtherEntity is not CameraZone zone)
        {
            return;
        }

        if (!_inZone)
        {
            _outside = Bounds;
            _inZone = true;
        }

        SetBounds(new Rect(zone.Position, zone.Size), SlideIn);
    }

    // Contacts settle first, so the probe's touching set is what it stands in now. Leaving into a zone it
    // still stands in hands the bounds to the last of them. A scene tearing down with the probe inside ends
    // the contact after this camera has left it.
    private void OnProbeExited(ColliderContact2D contact)
    {
        if (SceneOrNull is null || !_inZone)
        {
            return;
        }

        ReadOnlySpan<ColliderContact2D> touching = _probe.Touching;
        for (int i = touching.Length - 1; i >= 0; i--)
        {
            if (touching[i].OtherEntity is CameraZone held)
            {
                SetBounds(new Rect(held.Position, held.Size), SlideIn);

                return;
            }
        }

        _inZone = false;
        Bounds = _outside;
    }
}

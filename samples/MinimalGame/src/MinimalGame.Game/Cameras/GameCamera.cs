using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using MinimalGame.Game.Entities;

namespace MinimalGame.Game.Cameras;

/// <summary>
/// The follow camera: it owns the span the room is framed at and the feel of the follow, confines the
/// view to the room, and follows the player. A scene installs it and touches it no further.
/// </summary>
public sealed class GameCamera : Camera
{
    public GameCamera()
    {
        ViewportSize = World.ViewportSize;

        // The box the aim moves in without moving the camera. Its height holds the camera through an
        // ordinary 40 px jump. Widen it for a calmer camera, shrink it towards zero for a tighter one.
        Deadzone = new Vector2(16f, 96f);

        // Seconds the camera takes to catch its aim. Lower snaps, higher drifts.
        SmoothTime = 0.25f;
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        // A scene with no tile map spans nothing, and bounds of no size would pin the view.
        if (Scene.Size.X > 0f && Scene.Size.Y > 0f)
        {
            Bounds = new Rect(Vector2.Zero, Scene.Size);
        }

        Follow(Scene.FindSingle<Player>());
    }
}

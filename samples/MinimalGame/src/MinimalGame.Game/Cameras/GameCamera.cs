using System.Numerics;
using Capsule;
using Capsule.Scenes;
using MinimalGame.Game.Entities;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.Cameras;

/// <summary>
/// The follow camera: it owns the span the room is framed at and finds the player itself. A scene
/// installs it and touches it no further — the subject is discovered in <see cref="OnStart"/>,
/// which runs once every entity the scene was composed from has been added, and framing is settled
/// in <see cref="OnLateStep"/>, which runs after every entity has moved, so the player is never a
/// step ahead of the view.
/// <para>
/// A scene that holds no player installs no camera of its own; <see cref="MainMenu"/> spans the
/// plain camera it is given instead.
/// </para>
/// </summary>
public sealed class GameCamera : Camera
{
    // OnStart runs before any late step, so nothing reads this before the subject is found.
    private Player _subject = null!;

    public GameCamera() => ViewportSize = new Vector2(320f, 180f);

    /// <inheritdoc/>
    protected override void OnStart()
    {
        _subject = Scene!.FindSingle<Player>();

        // The room opens framed on the player rather than sweeping to it from the world origin.
        Teleport(_subject.Position);
    }

    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context) => Center = _subject.Position;
}

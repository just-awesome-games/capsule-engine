using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

// The follow: the subject, the deadzone its aim moves in, the lead ahead of it and the smoothing that
// carries the centre after them.
public partial class Camera
{
    // The point the smoothing chases. The deadzone is centred on it.
    private Vector2 _focus;

    // How far ahead of the subject the camera aims, eased toward the lookahead's target.
    private Vector2 _lead;

    /// <summary>The entity this camera follows, or null when it follows none.</summary>
    public Entity? Subject { get; private set; }

    /// <summary>
    /// The size of the box the camera's aim moves in without moving the camera, in world units, where
    /// zero, the default, follows tightly.
    /// </summary>
    public Vector2 Deadzone
    {
        get;

        set
        {
            Guard.NonNegative(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// How far ahead the camera aims, in seconds of the subject's speed past
    /// <see cref="LookaheadThreshold"/> on each axis, where zero, the default, never leads.
    /// </summary>
    /// <remarks>The lead eases toward that aim over <see cref="SmoothTime"/>, as the centre does.</remarks>
    public Vector2 Lookahead
    {
        get;

        set
        {
            Guard.NonNegative(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// The speed on each axis below which the camera does not look ahead, in world units per second,
    /// where zero, the default, looks ahead at any speed.
    /// </summary>
    public Vector2 LookaheadThreshold
    {
        get;

        set
        {
            Guard.NonNegative(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// How long the camera takes to catch its aim, in seconds, where zero, the default, locks on.
    /// </summary>
    /// <remarks>At a steady speed the camera trails by exactly this many seconds of travel.</remarks>
    public float SmoothTime
    {
        get;

        set
        {
            Guard.RequireSeconds(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Frames <paramref name="subject"/> from this step on, or holds the framing when it is null. A call
    /// before this camera's first settle cuts to a subject in this scene or in none yet, and a later one
    /// glides to it.
    /// </summary>
    /// <remarks>
    /// The subject never leaves the frame, whatever its speed. A subject outside this camera's scene, or
    /// paused or frozen, holds the camera until it steps in this scene again.
    /// </remarks>
    public void Follow(Entity? subject)
    {
        Subject = subject;
        ResetFollow(Center);

        if (subject is not null && !_settled && (subject.SceneOrNull is null || ReferenceEquals(subject.SceneOrNull, _scene)))
        {
            Teleport(subject.WorldPosition);
        }
    }

    private void ResetFollow(Vector2 center)
    {
        _focus = center;
        _lead = Vector2.Zero;
    }

    private void DrawDeadzone()
    {
        if (Subject is not null && Deadzone != Vector2.Zero)
        {
            DebugDraw.Rect(DebugDraw.Camera, new Rect(_focus - (Deadzone / 2f), Deadzone));
        }
    }

    // The hard edge measures the span the frame draws on output, before the offset and the shake move it.
    private void StepFollow(float seconds, Vector2 output)
    {
        if (Subject is not { } subject)
        {
            return;
        }

        if (!ReferenceEquals(subject.SceneOrNull, _scene))
        {
            return;
        }

        if (subject.Held || !(seconds > 0f))
        {
            return;
        }

        CameraView view = ToView();
        Vector2 span = HostLayout(view, output)?.Span ?? view.Size;

        // A teleported subject collapses its previous transform and reports no velocity.
        Vector2 position = subject.WorldPosition;
        Vector2 velocity = (position - subject.PreviousWorld.Position) / seconds;

        // The lead grows from zero at the threshold and eases in as the centre does. A quick turn then
        // cannot flip it in one step.
        Vector2 past = Vector2.Max(Vector2.Zero, Vector2.Abs(velocity) - LookaheadThreshold) * Lookahead;
        Vector2 target = new(MathF.CopySign(past.X, velocity.X), MathF.CopySign(past.Y, velocity.Y));
        _lead = SmoothTime > 0f ? _lead + ((target - _lead) * (seconds / (SmoothTime + seconds))) : target;
        Vector2 aim = position + _lead;
        Vector2 reach = Deadzone / 2f;
        _focus = Vector2.Clamp(_focus, aim - reach, aim + reach);

        // The implicit step of an exponential approach. At a steady speed it trails by exactly
        // SmoothTime seconds of travel whatever the step length.
        Center = SmoothTime > 0f ? Center + ((_focus - Center) * (seconds / (SmoothTime + seconds))) : _focus;

        // The hard edge keeps the subject inside the span at any speed.
        Vector2 half = span / 2f;
        Center = Vector2.Clamp(Center, position - half, position + half);
    }
}

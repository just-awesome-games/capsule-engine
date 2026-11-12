using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

// The follow: the subject, the point on it the camera aims at, the deadzone that aim moves in, the lead
// ahead of it, and the smoothing and the speed cap that carry the centre after them. The follow's own
// move each step lets an opening bounds edge keep pace with it.
public partial class Camera
{
    // The point the smoothing chases. The deadzone is centred on it, and each settle confines it as it
    // confines the view.
    private Vector2 _focus;

    // How far ahead of the subject the camera aims, eased toward the lookahead's target.
    private Vector2 _lead;

    // The unconfined centre the follow settled on at the previous step, or null when that step did not
    // follow or the follow has since reset.
    private Vector2? _followCenter;

    // How far the follow moved the unconfined centre this step. It is zero on a step that does not follow,
    // on a cut and on the first settle.
    private Vector2 _followMove;

    // A Follow before the first settle cuts to its subject when its scene finishes starting, or at that
    // settle when this camera was installed later.
    private bool _cutToSubject;

    /// <summary>The entity this camera follows, or null when it follows none.</summary>
    public Entity? Subject { get; private set; }

    /// <summary>
    /// The point the camera aims at relative to its subject's position, in world units, where zero, the
    /// default, aims at the position itself.
    /// </summary>
    /// <remarks>
    /// The <see cref="Deadzone"/>, the lookahead and the smoothing work from this point, and the cut to a
    /// new subject lands on it. The subject's own position still never leaves the frame inside
    /// <see cref="Bounds"/>. Unlike <see cref="Offset"/>, which moves the view after the bounds confine it,
    /// this moves what the follow aims at.
    /// </remarks>
    /// <example>
    /// A subject positioned at its feet, framed by a point 17 units above them:
    /// <code>
    /// FollowOffset = new Vector2(0f, -17f);
    /// </code>
    /// </example>
    public Vector2 FollowOffset
    {
        get;
        set => field = Guard.Finite(value, nameof(value));
    }

    /// <summary>
    /// The size of the box the camera's aim moves in without moving the camera, in world units, where
    /// zero, the default, follows tightly.
    /// </summary>
    /// <remarks>
    /// The box is centred on where the view settles, <see cref="Bounds"/> included. A view held at an
    /// edge moves once the aim leaves the box around that view, as far as the bounds allow.
    /// </remarks>
    public Vector2 Deadzone
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    }

    /// <summary>
    /// How far ahead the camera aims, in seconds of the subject's speed past
    /// <see cref="LookaheadThreshold"/> on each axis, where zero, the default, never leads.
    /// </summary>
    /// <remarks>The lead eases toward that aim over <see cref="SmoothTime"/>, as the centre does.</remarks>
    public Vector2 Lookahead
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    }

    /// <summary>
    /// The speed on each axis below which the camera does not look ahead, in world units per second,
    /// where zero, the default, looks ahead at any speed.
    /// </summary>
    public Vector2 LookaheadThreshold
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    }

    /// <summary>
    /// How long the camera takes to catch its aim, in seconds, where zero, the default, locks on.
    /// </summary>
    /// <remarks>At a steady speed the camera trails by exactly this many seconds of travel.</remarks>
    public float SmoothTime
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    }

    /// <summary>
    /// The fastest the follow moves the camera on each axis, in world units per second, where zero, the
    /// default, leaves that axis unlimited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A subject whose position jumps a few units in one step is reached by a pan at this speed rather
    /// than a cut. Each axis is capped on its own. A diagonal pan can move up to 1.41 times the cap. The cap
    /// measures from where the view settled at the previous step. A subject newly followed, or one resuming
    /// after it was paused or left the scene, is reached the same way.
    /// </para>
    /// <para>
    /// The cap applies to the centre with the lookahead's lead included. A camera that looks ahead needs a
    /// cap above the subject's top speed plus the rate its lead grows, or it trails. The subject's own
    /// position still never leaves the frame inside <see cref="Bounds"/>, and the bounds move the view
    /// faster than the cap when a closing edge or a <see cref="BoundsTransition.Snap"/> pushes it. A caller
    /// that moves the subject far away and wants the view there at once calls <see cref="Teleport"/>.
    /// </para>
    /// </remarks>
    public Vector2 MaxFollowSpeed
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    }

    /// <summary>
    /// Frames <paramref name="subject"/> from this step on, or holds the framing when it is null. A call
    /// before this camera's first settle cuts to the subject where it stands once the scene finishes
    /// starting, or at that settle for a camera installed later.
    /// </summary>
    /// <remarks>
    /// A later call glides to the subject. The cut goes only to a subject in this camera's scene or in
    /// none yet. Inside <see cref="Bounds"/> the subject never leaves the frame, whatever its speed. A
    /// subject outside this camera's scene, or paused or frozen, holds the camera until it steps in this
    /// scene again.
    /// </remarks>
    public void Follow(Entity? subject)
    {
        Subject = subject;
        ResetFollow(Center);
        _cutToSubject = subject is not null && !_settled;
    }

    private void ResetFollow(Vector2 center)
    {
        _focus = center;
        _lead = Vector2.Zero;
        _followCenter = null;
    }

    // The cut a Follow before the first settle asked for, taken where the subject stands by then.
    internal void CutToSubject()
    {
        if (!_cutToSubject)
        {
            return;
        }

        _cutToSubject = false;

        if (Subject is { } subject && (subject.SceneOrNull is null || ReferenceEquals(subject.SceneOrNull, _scene)))
        {
            Cut(subject.WorldPosition + FollowOffset);
        }
    }

    private void DrawDeadzone()
    {
        if (Subject is not null && Deadzone != Vector2.Zero)
        {
            DebugDraw.Rect(DebugDraw.Camera, Rect.FromCenter(_focus, Deadzone));
        }
    }

    // The hard edge measures the span the frame draws on output, before the offset and the shake move it.
    private void StepFollow(float seconds, Vector2 output)
    {
        _followMove = Vector2.Zero;

        if (Subject is not { } subject || !ReferenceEquals(subject.SceneOrNull, _scene) || subject.Held || !(seconds > 0f))
        {
            _followCenter = null;
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
        Vector2 aim = position + FollowOffset + _lead;
        Vector2 reach = Deadzone / 2f;
        _focus = Vector2.Clamp(_focus, aim - reach, aim + reach);

        // The cap measures from the settled view, not from _followCenter. A step after a Follow, a cut or a
        // held subject has no previous follow, and the view would jump on it.
        Vector2 from = Center;

        // The implicit step of an exponential approach. At a steady speed it trails by exactly
        // SmoothTime seconds of travel whatever the step length.
        Center = SmoothTime > 0f ? Center + ((_focus - Center) * (seconds / (SmoothTime + seconds))) : _focus;
        Center = from + CapFollow(Center - from, MaxFollowSpeed * seconds);

        // The hard edge keeps the subject inside the span at any speed, and outranks the cap.
        Vector2 half = span / 2f;
        Center = Vector2.Clamp(Center, position - half, position + half);

        if (_followCenter is { } previous && _settled && !_cut)
        {
            _followMove = Center - previous;
        }

        _followCenter = Center;
    }

    // The move shortened on each axis to that axis's cap, where a cap of zero leaves the axis free.
    private static Vector2 CapFollow(Vector2 move, Vector2 cap) => new(
        cap.X > 0f ? Math.Clamp(move.X, -cap.X, cap.X) : move.X,
        cap.Y > 0f ? Math.Clamp(move.Y, -cap.Y, cap.Y) : move.Y);
}

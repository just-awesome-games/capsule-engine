using System.Numerics;
using Capsule.Animation;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

// The bounds: the rect the view settles inside, and the chase or timed transition that carries the view
// into a changed one. The chase moves an opening edge out beyond the follow's own move and keeps pace
// with a moving subject.
public partial class Camera
{
    // A chased edge this close to its target lands on it, in world units.
    private const float SettledEdge = 1e-3f;

    private static readonly Rect Unbounded = new(
        float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);

    // The rect confining the view this step. It is Bounds once any change has eased in, and null leaves
    // the view free.
    private Rect? _confine;

    // The rect that confined the view at the previous step. While it differs from _confine both settled
    // centres are already confined, and a frame between them is drawn unclamped.
    private Rect? _previousConfine;

    // The transition EaseBounds started: whether one is running, its curve, its length, the seconds it
    // has run and the rect it runs from. The rect is taken at its first settle, where the view it starts
    // from is known. A length of zero snaps.
    private bool _transitioning;
    private Ease _transitionEase;
    private float _transitionSeconds;
    private float _transitionElapsed;
    private Rect? _transitionFrom;

    /// <summary>
    /// The world rect the view stays inside, or null, the default, to leave it free. Each step settles
    /// <see cref="Center"/> where the view fits inside it, centred on an axis where the view is larger.
    /// </summary>
    /// <remarks>
    /// Bounds win over the follow. A subject that leaves them leaves the frame. An infinite edge leaves
    /// that side open. A change chases in over <see cref="BoundsSmoothTime"/>, capped by
    /// <see cref="BoundsMaxSpeed"/>, or as <see cref="EaseBounds"/> times it. <see cref="Offset"/> and the
    /// shake move the view after it is confined.
    /// </remarks>
    /// <example>
    /// <code>
    /// Bounds = new Rect(Vector2.Zero, Scene.Size);
    /// Bounds = new Rect(0f, float.NegativeInfinity, Scene.Size.X, Scene.Size.Y); // no ceiling
    /// Bounds = Bounds.Value with { Right = arena.Right };                          // one edge closes
    /// </code>
    /// </example>
    public Rect? Bounds
    {
        get;

        set
        {
            if (value is { } rect && (float.IsNaN(rect.Left) || float.IsNaN(rect.Top) || float.IsNaN(rect.Right) || float.IsNaN(rect.Bottom)))
            {
                throw new ArgumentException("Bounds has a NaN edge. Use an infinite edge to leave a side open.", nameof(value));
            }

            field = value;
            _transitioning = false;
        }
    }

    /// <summary>
    /// How long the view takes to settle into changed <see cref="Bounds"/>, in seconds, where zero, the
    /// default, snaps.
    /// </summary>
    /// <remarks>
    /// An edge closing on the view starts at the view's own edge and pushes it. An opening edge moves out
    /// from where it was, or from where the follow carries the view this step when that is further out.
    /// The view then keeps pace with its subject and gains on it. <see cref="BoundsMaxSpeed"/> caps the
    /// chase beyond that move. An edge opening to infinity, null bounds, <see cref="Teleport"/> and this
    /// camera's first step in a scene take effect at once.
    /// </remarks>
    public float BoundsSmoothTime
    {
        get;

        set
        {
            Guard.RequireSeconds(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// The cap on the chase that carries the view into changed <see cref="Bounds"/>, in world units per
    /// second, where zero, the default, leaves the chase uncapped.
    /// </summary>
    /// <remarks>
    /// A closing edge pushes the view at no more than this speed. An opening edge moves out at up to this
    /// speed beyond the follow's own move. It keeps pace with a moving subject and gains on it at this
    /// speed. With <see cref="BoundsSmoothTime"/> at zero the chase runs at exactly this speed and lands
    /// once it is within one step. With both set the smoothed chase runs no faster than this. With both at
    /// zero a change snaps. <see cref="EaseBounds"/> ignores it.
    /// </remarks>
    /// <example>
    /// A scroll into each new screen at 2 units a step at 60 steps a second:
    /// <code>
    /// BoundsSmoothTime = 0f;
    /// BoundsMaxSpeed = 120f;
    /// </code>
    /// </example>
    public float BoundsMaxSpeed
    {
        get;

        set
        {
            Guard.NonNegative(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Moves the view into <paramref name="bounds"/> over <paramref name="seconds"/> along
    /// <paramref name="ease"/>, in place of the <see cref="BoundsSmoothTime"/> and
    /// <see cref="BoundsMaxSpeed"/> chase.
    /// </summary>
    /// <remarks>
    /// <see cref="Bounds"/> reads <paramref name="bounds"/> at once. The move starts from the view where it
    /// stands, as the chase does. Setting <see cref="Bounds"/> and another call take over from wherever it
    /// has reached. Zero seconds, <see cref="Teleport"/>, this camera's first step in a scene and an edge
    /// opening to infinity land at once.
    /// </remarks>
    /// <example>
    /// A boss arena framed on the same curve as a zoom tween that widens to show it, 30 ticks being half
    /// a second at 60 steps a second:
    /// <code>
    /// Scene.Camera.EaseBounds(arena, 0.5f, Ease.InOutSine);
    /// _zoom.Start(30, Ease.InOutSine);
    /// </code>
    /// </example>
    public void EaseBounds(Rect bounds, float seconds, Ease ease)
    {
        Guard.RequireSeconds(seconds, nameof(seconds));
        Guard.RequireEase(ease, nameof(ease));
        Bounds = bounds;
        _transitioning = true;
        _transitionEase = ease;
        _transitionSeconds = seconds;
        _transitionElapsed = 0f;
        _transitionFrom = null;
    }

    // What a frame confines to. Before the first settle it is Bounds as they stand. A frame confines only
    // against a rect that held for both steps it draws between.
    private Rect? DrawnBounds => !_settled ? Bounds : _confine == _previousConfine ? _confine : null;

    // Moves the confining rect toward Bounds by the timed transition, the chase or a snap, and settles
    // Center inside it at the span the frame draws on output. It runs after the follow. The bounds win
    // over it.
    private void StepBounds(float seconds, Vector2 output)
    {
        CameraView view = ToView();
        Vector2 half = Half(view, output);

        // The previous step's view at the span it was drawn at.
        Vector2 previousHalf = Half(view with { Size = view.PreviousSize }, output);
        Rect standing = new(PreviousCenter - previousHalf, previousHalf * 2f);

        if (_cut || !_settled)
        {
            _transitioning = false;
        }

        if (Bounds is { } target && _transitioning && _transitionSeconds > 0f)
        {
            Rect from = _transitionFrom ??= Start(_confine ?? Unbounded, target, standing, Vector2.Zero);
            _transitionElapsed += seconds;

            // The step ending within half a step of the length lands. Float drift in the sum adds no step.
            if (_transitionSeconds - _transitionElapsed < seconds / 2f)
            {
                _transitioning = false;
                _confine = target;
            }
            else
            {
                _confine = Along(from, target, Easing.Apply(_transitionEase, _transitionElapsed / _transitionSeconds));
            }
        }
        else if (Bounds is { } chased && !_transitioning && _settled && !_cut && (BoundsSmoothTime > 0f || BoundsMaxSpeed > 0f))
        {
            Rect from = Start(_confine ?? Unbounded, chased, standing, _followMove);
            _confine = Chase(from, chased, seconds / (BoundsSmoothTime + seconds), BoundsMaxSpeed * seconds);
        }
        else
        {
            _transitioning = false;

            // A snap confines the step's first frame too. The frame does not sweep in from outside.
            if (!_settled || _confine != Bounds)
            {
                _confine = _previousConfine = Bounds;
                PreviousCenter = Confined(PreviousCenter, previousHalf);
            }
        }

        Center = Confined(Center, half);

        // The deadzone measures from a centre the view can settle on. A focus left past the rect would
        // hold the camera on the far side of the deadzone once the rect opens.
        _focus = Confined(_focus, half);
    }

    private Vector2 Half(in CameraView view, Vector2 output) => (HostLayout(view, output)?.Span ?? view.Size) / 2f;

    private Vector2 Confined(Vector2 center, Vector2 half) =>
        _confine is { } confine && ViewportSize.X > 0f && ViewportSize.Y > 0f
            ? CameraView.Confine(center, half, confine)
            : center;

    // Each edge starts no further in than the view standing inside the old rect. A closing edge pushes the
    // view from where it is. An opening one starts where it was or where the follow's own move this step
    // carries the standing view's edge, whichever is further out. A view chasing a falling subject then
    // falls with it and gains on it.
    private static Rect Start(in Rect from, in Rect to, in Rect standing, Vector2 move) => new(
        StartLow(from.Left, to.Left, standing.Left, move.X),
        StartLow(from.Top, to.Top, standing.Top, move.Y),
        StartHigh(from.Right, to.Right, standing.Right, move.X),
        StartHigh(from.Bottom, to.Bottom, standing.Bottom, move.Y));

    // A left or top edge. It opens toward negative infinity.
    private static float StartLow(float from, float to, float standing, float move) => to < from
        ? MathF.Min(from, MathF.Max(to, standing + MathF.Min(move, 0f)))
        : MathF.Max(from, MathF.Min(to, standing));

    // A right or bottom edge. It opens toward positive infinity.
    private static float StartHigh(float from, float to, float standing, float move) => to > from
        ? MathF.Max(from, MathF.Min(to, standing + MathF.Max(move, 0f)))
        : MathF.Min(from, MathF.Max(to, standing));

    private static Rect Chase(in Rect from, in Rect to, float blend, float maxStep) => new(
        Toward(from.Left, to.Left, blend, maxStep),
        Toward(from.Top, to.Top, blend, maxStep),
        Toward(from.Right, to.Right, blend, maxStep),
        Toward(from.Bottom, to.Bottom, blend, maxStep));

    // The rect a timed transition has reached at eased progress k. An edge opening to infinity is there.
    private static Rect Along(in Rect from, in Rect to, float k) => new(
        Along(from.Left, to.Left, k),
        Along(from.Top, to.Top, k),
        Along(from.Right, to.Right, k),
        Along(from.Bottom, to.Bottom, k));

    private static float Along(float from, float to, float k) =>
        from == to || float.IsInfinity(to) ? to : from + ((to - from) * k);

    // The implicit step of an exponential approach, no longer than maxStep when that is positive. A blend
    // of one with a cap moves at constant speed. An edge the step cannot move lands on its target. The
    // chase then ends far from the origin too. An edge that comes within SettledEdge lands only when the
    // cap allows the whole gap. An edge opening to infinity is there.
    private static float Toward(float edge, float target, float blend, float maxStep)
    {
        if (edge == target || float.IsInfinity(target))
        {
            return target;
        }

        float step = (target - edge) * blend;
        if (maxStep > 0f && MathF.Abs(step) > maxStep)
        {
            step = MathF.CopySign(maxStep, step);
        }

        float next = edge + step;

        bool settles = MathF.Abs(target - next) < SettledEdge && !(maxStep > 0f && MathF.Abs(target - edge) > maxStep);

        return next == edge || settles ? target : next;
    }

    // An open edge is drawn a view's width past the view. Only the finite edges show.
    private void DrawBounds()
    {
        if ((_settled ? _confine : Bounds) is not { } bounds)
        {
            return;
        }

        Rect view = VisibleRegion;
        Vector2 reach = view.Size;

        DebugDraw.Rect(DebugDraw.Camera, new Rect(
            float.IsFinite(bounds.Left) ? bounds.Left : view.Left - reach.X,
            float.IsFinite(bounds.Top) ? bounds.Top : view.Top - reach.Y,
            float.IsFinite(bounds.Right) ? bounds.Right : view.Right + reach.X,
            float.IsFinite(bounds.Bottom) ? bounds.Bottom : view.Bottom + reach.Y));
    }
}

using System.Numerics;
using Capsule.Animation;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

// The bounds: the rect the view settles inside, and the chase or timed transition that carries the view
// into a changed one.
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
    /// that side open. A change eases in over <see cref="BoundsSmoothTime"/>, or as <see cref="EaseBounds"/>
    /// times it. <see cref="Offset"/> and the shake move the view after it is confined.
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
    /// An edge closing on the view starts at the view's own edge and pushes it. An opening edge eases out
    /// from where it was. An edge opening to infinity, null bounds, <see cref="Teleport"/> and this
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
    /// Moves the view into <paramref name="bounds"/> over <paramref name="seconds"/> along
    /// <paramref name="ease"/>, in place of the <see cref="BoundsSmoothTime"/> chase.
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
            Rect from = _transitionFrom ??= Start(_confine ?? Unbounded, target, standing);
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
        else if (Bounds is { } chased && !_transitioning && _settled && !_cut && BoundsSmoothTime > 0f)
        {
            _confine = Chase(Start(_confine ?? Unbounded, chased, standing), chased, seconds / (BoundsSmoothTime + seconds));
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
    // view from where it is. An opening one moves out from where it was.
    private static Rect Start(in Rect from, in Rect to, in Rect standing) => new(
        MathF.Max(from.Left, MathF.Min(to.Left, standing.Left)),
        MathF.Max(from.Top, MathF.Min(to.Top, standing.Top)),
        MathF.Min(from.Right, MathF.Max(to.Right, standing.Right)),
        MathF.Min(from.Bottom, MathF.Max(to.Bottom, standing.Bottom)));

    private static Rect Chase(in Rect from, in Rect to, float blend) => new(
        Toward(from.Left, to.Left, blend),
        Toward(from.Top, to.Top, blend),
        Toward(from.Right, to.Right, blend),
        Toward(from.Bottom, to.Bottom, blend));

    // The rect a timed transition has reached at eased progress k. An edge opening to infinity is there.
    private static Rect Along(in Rect from, in Rect to, float k) => new(
        Along(from.Left, to.Left, k),
        Along(from.Top, to.Top, k),
        Along(from.Right, to.Right, k),
        Along(from.Bottom, to.Bottom, k));

    private static float Along(float from, float to, float k) =>
        from == to || float.IsInfinity(to) ? to : from + ((to - from) * k);

    // The implicit step of an exponential approach. An edge the step cannot move lands on its target. The
    // chase then ends far from the origin too.
    private static float Toward(float edge, float target, float blend)
    {
        if (edge == target)
        {
            return target;
        }

        float next = edge + ((target - edge) * blend);

        return next == edge || MathF.Abs(target - next) < SettledEdge ? target : next;
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

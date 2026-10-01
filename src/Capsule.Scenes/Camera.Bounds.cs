using System.Numerics;
using Capsule.Animation;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

// The bounds: the rect the view settles inside, and the transition that carries the view into a changed
// one. The chase moves an opening edge out beyond the follow's own move and keeps pace with a moving
// subject.
public partial class Camera
{
    // A chased edge this close to its target lands on it, in world units.
    private const float SettledEdge = 1e-3f;

    private static readonly Rect Unbounded = new(
        float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);

    private Rect? _bounds;

    // The rect confining the view this step. It is Bounds once any change has landed, and null leaves
    // the view free.
    private Rect? _confine;

    // The rect that confined the view at the previous step. While it differs from _confine both settled
    // centres are already confined, and a frame between them is drawn unclamped.
    private Rect? _previousConfine;

    // The transition carrying the latest change, taken when the change was made.
    private BoundsTransition _carry;

    // Whether an eased change is running, the seconds it has run and the rect it runs from. The rect is
    // taken at its first settle, where the view it starts from is known.
    private bool _easing;
    private float _easeElapsed;
    private Rect? _easeFrom;

    /// <summary>
    /// The world rect the view stays inside, or null, the default, to leave it free. Each step settles
    /// <see cref="Center"/> where the view fits inside it, centred on an axis where the view is larger.
    /// </summary>
    /// <remarks>
    /// Bounds win over the follow. A subject that leaves them leaves the frame. An infinite edge leaves
    /// that side open. A change is carried by <see cref="BoundsTransition"/>, or by the transition
    /// <see cref="SetBounds"/> passes. Setting the rect the bounds already hold changes nothing, and a
    /// running transition carries on. <see cref="Offset"/> and the shake move the view after it is
    /// confined.
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
        get => _bounds;
        set => SetBounds(value, BoundsTransition);
    }

    /// <summary>
    /// How a change made through the <see cref="Bounds"/> setter is carried into the view, where
    /// <see cref="BoundsTransition.Snap"/>, the default, moves it at once.
    /// </summary>
    /// <remarks>
    /// A change takes the transition standing when it is made and keeps it until it lands or another
    /// change replaces it. Setting this during a change leaves that change as it runs.
    /// </remarks>
    public BoundsTransition BoundsTransition { get; set; }

    /// <summary>
    /// Changes <see cref="Bounds"/> to <paramref name="bounds"/>, carried by <paramref name="transition"/>
    /// in place of the standing <see cref="BoundsTransition"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Bounds"/> reads <paramref name="bounds"/> at once. The change takes over from wherever a
    /// running one has reached. Passing the rect the bounds already hold changes nothing, and a running
    /// transition carries on.
    /// </remarks>
    /// <example>
    /// A door that cuts to the next room under a camera that otherwise settles softly:
    /// <code>Scene.Camera.SetBounds(nextRoom, BoundsTransition.Snap);</code>
    /// </example>
    public void SetBounds(Rect? bounds, BoundsTransition transition)
    {
        if (bounds is { } rect && (float.IsNaN(rect.Left) || float.IsNaN(rect.Top) || float.IsNaN(rect.Right) || float.IsNaN(rect.Bottom)))
        {
            throw new ArgumentException("Bounds has a NaN edge. Use an infinite edge to leave a side open.", nameof(bounds));
        }

        if (bounds == _bounds)
        {
            return;
        }

        _bounds = bounds;
        _carry = transition;
        _easing = transition.IsEased;
        _easeElapsed = 0f;
        _easeFrom = null;
    }

    // What a frame confines to. Before the first settle it is Bounds as they stand. A frame confines only
    // against a rect that held for both steps it draws between.
    private Rect? DrawnBounds => !_settled ? Bounds : _confine == _previousConfine ? _confine : null;

    // Moves the confining rect toward Bounds by the change's transition and settles Center inside it at the
    // span the frame draws on output. It runs after the follow. The bounds win over it.
    private void StepBounds(float seconds, Vector2 output)
    {
        CameraView view = ToView();
        Vector2 half = Half(view, output);

        // The previous step's view at the span it was drawn at.
        Vector2 previousHalf = Half(view with { Size = view.PreviousSize }, output);
        Rect standing = new(PreviousCenter - previousHalf, previousHalf * 2f);

        bool moves = _settled && !_cut;
        if (Bounds is { } target && moves && _easing)
        {
            Rect from = _easeFrom ??= Start(_confine ?? Unbounded, target, standing, Vector2.Zero);
            _easeElapsed += seconds;

            // The step ending within half a step of the length lands. Float drift in the sum adds no step.
            if (_carry.Seconds - _easeElapsed < seconds / 2f)
            {
                _easing = false;
                _confine = target;
            }
            else
            {
                _confine = Along(from, target, Easing.Apply(_carry.Ease, _easeElapsed / _carry.Seconds));
            }
        }
        else if (Bounds is { } chased && moves && _carry.IsChase)
        {
            Rect from = Start(_confine ?? Unbounded, chased, standing, _followMove);
            _confine = Chase(from, chased, seconds / (_carry.Seconds + seconds), _carry.MaxSpeed * seconds);
        }
        else
        {
            _easing = false;

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

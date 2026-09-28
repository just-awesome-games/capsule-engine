using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

// The bounds: the rect the view settles inside, and the ease that carries the view into a changed one.
public partial class Camera
{
    // An eased edge this close to its target lands on it, in world units.
    private const float SettledEdge = 1e-3f;

    private static readonly Rect Unbounded = new(
        float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);

    // The rect confining the view this step. It is Bounds once any change has eased in, and null leaves
    // the view free.
    private Rect? _confine;

    // The rect that confined the view at the previous step. A frame between the two steps confines to the
    // rect spanning both, which holds both settled centres, so an easing edge never clamps the interpolation.
    private Rect? _previousConfine;

    /// <summary>
    /// The world rect the view stays inside, or null, the default, to leave it free. Each step settles
    /// <see cref="Center"/> where the view fits inside it, centred on an axis where the view is larger.
    /// </summary>
    /// <remarks>
    /// Bounds win over the follow, so a subject that leaves them leaves the frame. An infinite edge leaves
    /// that side open. A change eases in over <see cref="BoundsSmoothTime"/>. <see cref="Offset"/> and the
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
        }
    }

    /// <summary>
    /// How long the view takes to settle into changed <see cref="Bounds"/>, in seconds, where zero, the
    /// default, snaps.
    /// </summary>
    /// <remarks>
    /// An edge closing on the view starts at the view's own edge and pushes it, and an opening edge eases
    /// out from where it was. An edge opening to infinity, null bounds and <see cref="Teleport"/> take
    /// effect at once.
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

    // What a frame confines to. Before the first settle it is Bounds as they stand.
    private Rect? DrawnBounds => !_settled
        ? Bounds
        : _confine is { } now && _previousConfine is { } before
            ? new Rect(
                MathF.Min(now.Left, before.Left),
                MathF.Min(now.Top, before.Top),
                MathF.Max(now.Right, before.Right),
                MathF.Max(now.Bottom, before.Bottom))
            : null;

    // Eases the confining rect toward Bounds and settles Center inside it, at the span the frame draws on
    // output. It runs after the follow, so the bounds win over it.
    private void StepBounds(float seconds, Vector2 output)
    {
        CameraView view = ToView();
        Vector2 half = (HostLayout(view, output)?.Span ?? view.Size) / 2f;

        if (Bounds is { } target && _settled && !_cut && BoundsSmoothTime > 0f)
        {
            Rect standing = new(PreviousCenter - half, half * 2f);
            _confine = Ease(_confine ?? Unbounded, target, standing, seconds / (BoundsSmoothTime + seconds));
        }
        else
        {
            // A snap confines the step's first frame too, so the frame does not sweep in from outside.
            _confine = _previousConfine = Bounds;
            PreviousCenter = Confined(PreviousCenter, half);
        }

        Center = Confined(Center, half);
    }

    private Vector2 Confined(Vector2 center, Vector2 half) =>
        _confine is { } confine && ViewportSize.X > 0f && ViewportSize.Y > 0f
            ? CameraView.Confine(center, half, confine)
            : center;

    // Each edge starts no further in than the view standing inside the old rect, so a closing edge pushes
    // the view from where it is and an opening one eases out from where it was.
    private static Rect Ease(in Rect from, in Rect to, in Rect standing, float blend) => new(
        Toward(MathF.Max(from.Left, MathF.Min(to.Left, standing.Left)), to.Left, blend),
        Toward(MathF.Max(from.Top, MathF.Min(to.Top, standing.Top)), to.Top, blend),
        Toward(MathF.Min(from.Right, MathF.Max(to.Right, standing.Right)), to.Right, blend),
        Toward(MathF.Min(from.Bottom, MathF.Max(to.Bottom, standing.Bottom)), to.Bottom, blend));

    // The implicit step of an exponential approach. An edge the step cannot move lands, so the ease ends
    // far from the origin too.
    private static float Toward(float edge, float target, float blend)
    {
        if (edge == target)
        {
            return target;
        }

        float next = edge + ((target - edge) * blend);

        return next == edge || MathF.Abs(target - next) < SettledEdge ? target : next;
    }

    // An open edge is drawn a view's width past the view, so only the finite edges show.
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

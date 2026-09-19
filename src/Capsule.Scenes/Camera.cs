using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// A scene's world-space viewport. Movement interpolates, and <see cref="Teleport"/> cuts without
/// interpolating. A non-positive <see cref="ViewportSize"/> draws nothing. A scene installs a subclass to
/// keep framing in one place: the subclass finds its subject in <see cref="OnStart"/> and settles its
/// framing in <see cref="OnLateStep"/>.
/// </summary>
/// <example>
/// <code>
/// public sealed class GameCamera : Camera
/// {
///     private Player _subject = null!;
///
///     public GameCamera() =&gt; ViewportSize = World.ViewportSize;
///
///     protected override void OnStart()
///     {
///         _subject = Scene.FindSingle&lt;Player&gt;();
///         Teleport(_subject.Position);
///     }
///
///     protected override void OnLateStep(in StepContext context) =&gt; Center = _subject.Position;
/// }
/// </code>
/// </example>
public class Camera
{
    private bool _started;
    private Scene? _scene;

    /// <summary>The point the viewport is centred on, in world units.</summary>
    public Vector2 Center { get; set; }

    /// <summary>
    /// <see cref="Center"/> at the previous fixed step, in world units. The engine saves it.
    /// </summary>
    public Vector2 PreviousCenter { get; internal set; }

    /// <summary>
    /// How many world units the viewport spans. Zero until the scene or its camera sets it, and a
    /// non-positive span draws nothing.
    /// </summary>
    public Vector2 ViewportSize { get; set; }

    /// <summary>
    /// How <see cref="ViewportSize"/> adapts to an output whose aspect ratio differs from it. Defaults to
    /// <see cref="ViewportFit.Letterbox"/>, which shows that span and nothing else.
    /// </summary>
    public ViewportFit Fit { get; set; }

    /// <summary>
    /// A world rect the visible region must stay inside, applied after the fit resolves. The region is
    /// clamped inside these bounds on each axis, and centred on an axis where it is larger than the bounds.
    /// Null, the default, leaves the view free. <see cref="Center"/> keeps the raw framing target, so the
    /// clamping affects only what is drawn.
    /// </summary>
    public Rect? Bounds { get; set; }

    /// <summary>
    /// The camera corner at which every entity sits where it was authored, whatever its
    /// <see cref="Entity.ScrollFactor"/>. An entity with factor <c>f</c> draws as if the camera's corner sat
    /// at <c>ScrollOrigin + (Corner - ScrollOrigin) * f</c>, where Corner is the top-left of the world rect
    /// the frame places. Zero, the default, suits a room whose first screen is at the world origin. Set it
    /// for a room elsewhere in the world.
    /// </summary>
    public Vector2 ScrollOrigin
    {
        get;

        set
        {
            Guard.Finite(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// The world rect the frame draws: <see cref="ViewportSize"/> centred on <see cref="Center"/> and
    /// clamped to <see cref="Bounds"/>. The engine owns it and settles it once per step, right after
    /// <see cref="OnLateStep"/>. An entity or component that reads it during its own step sees the
    /// region of the frame last drawn. It reads empty before the first late step of the scene this camera
    /// frames, and whenever <see cref="ViewportSize"/> is not positive on both axes.
    /// <para>
    /// This is the settled framing. The renderer interpolates between the previous step's region and this
    /// one, and a <see cref="Fit"/> other than <see cref="ViewportFit.Letterbox"/> can reveal world beyond
    /// it on an output whose aspect ratio calls for that. The output's aspect ratio never reaches the
    /// simulation.
    /// </para>
    /// </summary>
    public Rect VisibleRegion { get; private set; }

    /// <summary>The scene this camera frames.</summary>
    /// <exception cref="InvalidOperationException">This camera frames no scene.</exception>
    public Scene Scene => _scene ?? throw new InvalidOperationException(
        "This Camera frames no scene. Reach the scene from OnStart onward.");

    /// <summary>
    /// The scene this camera frames, or null while it frames none. A camera installed in a scene that
    /// has not opened its camera yet takes the handle when that scene does.
    /// </summary>
    public Scene? SceneOrNull
    {
        get => _scene;

        // Taking or losing the handle reopens framing: a camera carries no region into or out of a
        // scene, so VisibleRegion is empty until this camera's next late step settles it.
        internal set
        {
            _scene = value;
            VisibleRegion = default;
        }
    }

    /// <summary>Cuts to <paramref name="center"/>, with no interpolation from the old centre.</summary>
    public void Teleport(Vector2 center)
    {
        Center = center;
        PreviousCenter = center;
    }

    /// <summary>
    /// Settles this camera's framing for the step. Runs after every entity and component has
    /// stepped, after contacts settle and after the scene's own <see cref="Scene.OnLateStep"/>,
    /// before the step's deferred adds and removes land and before the frame view is rewritten.
    /// </summary>
    protected internal virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>
    /// Runs once for this camera's lifetime, before its first late step, and not again when the camera
    /// is reinstalled. The scene and every entity it holds have started by then, so find the subject to
    /// follow here. A camera installed in a scene that has already opened its camera runs this as it is
    /// installed.
    /// </summary>
    protected internal virtual void OnStart()
    {
    }

    // Draws Bounds on the Camera channel when the camera has any. The visible region is the frame's
    // own edges and says nothing.
    internal void OnDebugDraw()
    {
        if (Bounds is { } bounds)
        {
            DebugDraw.Rect(DebugDraw.Camera, bounds);
        }
    }

    internal void SavePrevious() => PreviousCenter = Center;

    // What the renderer draws this camera as, and what the simulation measures visibility against.
    internal CameraView ToView() => new(PreviousCenter, Center, ViewportSize, Fit, Bounds, ScrollOrigin);

    // The drawing derivation itself, asked at the end of the step and with no output to measure, so
    // the span is the declared one and every fit resolves to it.
    internal void SettleVisibleRegion() => VisibleRegion = ToView().Resolve(1f, default);

    internal void RunStart()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        OnStart();
    }
}

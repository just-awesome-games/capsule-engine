using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// A scene's world-space viewport. Its centre, zoom and offset interpolate between steps, and
/// <see cref="Teleport"/> cuts without interpolating. A non-positive <see cref="ViewportSize"/> draws
/// nothing. A scene installs a subclass to keep framing in one place: the subclass picks its subject in
/// <see cref="OnStart"/> and steers the framing in <see cref="OnLateStep"/> when it needs to.
/// </summary>
/// <example>
/// <code>
/// public sealed class GameCamera : Camera
/// {
///     public GameCamera()
///     {
///         ViewportSize = World.ViewportSize;
///         Deadzone = new Vector2(16f, 96f);
///         SmoothTime = 0.25f;
///     }
///
///     protected override void OnStart() =&gt; Follow(Scene.FindSingle&lt;Player&gt;());
/// }
/// </code>
/// </example>
public partial class Camera
{
    private bool _started;
    private Scene? _scene;

    // Whether this camera has settled in its scene. A Follow before then is a cut.
    private bool _settled;

    // Collapses the interpolated framing at the next settle.
    private bool _cut;

    private float _previousZoom = 1f;
    private Vector2 _previousOffset;

    // The canvas to world conversion the last settle resolved, or null before one has.
    private CanvasMap? _canvasMap;

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
    /// How <see cref="ViewportSize"/> adapts to an output whose aspect ratio differs from it, defaulting to
    /// <see cref="ViewportFit.Letterbox"/>.
    /// </summary>
    public ViewportFit Fit { get; set; }

    /// <summary>
    /// A world rect the visible region must stay inside, applied after the fit resolves. The region is
    /// clamped inside these bounds on each axis, and centred on an axis where it is larger than the bounds.
    /// Null, the default, leaves the view free. <see cref="Center"/> keeps the raw framing target, so the
    /// clamping affects only what is drawn.
    /// </summary>
    public Rect? Bounds { get; set; }

    /// <summary>How many times the view magnifies the world, defaulting to 1.</summary>
    /// <remarks>At 2 it spans half of <see cref="ViewportSize"/>.</remarks>
    public float Zoom
    {
        get;

        set
        {
            Guard.Positive(value, nameof(value));
            field = value;
        }
    } = 1f;

    /// <summary>
    /// How far the drawn view is moved after <see cref="Bounds"/> confine it, in world units. The game
    /// owns it, and the shake adds to it.
    /// </summary>
    /// <example>
    /// A recoil the game builds on it, a spring that kicks and settles:
    /// <code>
    /// private Vector2 _kick, _kickVelocity;
    ///
    /// public void Recoil(Vector2 push) =&gt; _kickVelocity += push;
    ///
    /// protected override void OnLateStep(in StepContext context)
    /// {
    ///     _kickVelocity += ((-_kick * 900f) - (_kickVelocity * 40f)) * context.DeltaSeconds;
    ///     _kick += _kickVelocity * context.DeltaSeconds;
    ///     Offset = _kick;
    /// }
    /// </code>
    /// </example>
    public Vector2 Offset
    {
        get;

        set
        {
            Guard.Finite(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// The camera corner at which every entity sits where it was authored, whatever its
    /// <see cref="Entity.ScrollFactor"/>. An entity with factor <c>f</c> draws as if the camera's corner sat
    /// at <c>ScrollOrigin + (Corner - ScrollOrigin) * f</c>, where Corner is the top-left of the world rect
    /// the frame places. Set it for a room whose first screen is not at the world origin.
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
    /// The world rect the frame draws: <see cref="ViewportSize"/> over <see cref="Zoom"/>, centred on
    /// <see cref="Center"/>, clamped to <see cref="Bounds"/> and moved by <see cref="Offset"/> and the
    /// shake. The engine owns it and settles it once per step, after the follow and the shake. An entity
    /// or component that reads it during its own step sees the region the previous step settled. It reads
    /// empty before the first late step of the scene this camera frames, and whenever
    /// <see cref="ViewportSize"/> is not positive on both axes.
    /// <para>
    /// This is the settled framing, resolved against the output the host hands each step. Under every
    /// <see cref="Fit"/> it is the rect the host draws at the settled centre, and a step handed no
    /// output resolves the declared span. The renderer interpolates between the previous step's region
    /// and this one.
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
            _canvasMap = null;
            _settled = false;
        }
    }

    /// <summary>
    /// The world point drawn under <paramref name="canvasPoint"/>, a position in canvas pixels such as
    /// <see cref="Capsule.Input.InputState.Pointer"/>.
    /// </summary>
    /// <remarks>
    /// The conversion answers for the framing the last step settled, the one
    /// <see cref="VisibleRegion"/> holds. The host draws between that framing and the one before it.
    /// Before this camera's first settle it answers for the camera as it stands, letterboxed over
    /// <see cref="ViewportSize"/>. While <see cref="ViewportSize"/> is not positive on both axes nothing
    /// is drawn, and a canvas point converts to the same world point.
    /// </remarks>
    /// <example>
    /// <code>
    /// Vector2 target = Scene.Camera.CanvasToWorld(context.Input.Pointer);
    /// Vector2 aim = Vector2.Normalize(target - Muzzle.WorldPosition);
    /// </code>
    /// </example>
    public Vector2 CanvasToWorld(Vector2 canvasPoint) => CanvasToWorld(canvasPoint, Vector2.One);

    /// <summary>
    /// The point drawn under <paramref name="canvasPoint"/> on the layer of an entity whose
    /// <see cref="Entity.ScrollFactor"/> is <paramref name="scrollFactor"/>, in that entity's world units.
    /// </summary>
    /// <remarks>It answers for the settled framing, as <see cref="CanvasToWorld(Vector2)"/> does.</remarks>
    public Vector2 CanvasToWorld(Vector2 canvasPoint, Vector2 scrollFactor)
    {
        Guard.Finite(canvasPoint, nameof(canvasPoint));
        Guard.Finite(scrollFactor, nameof(scrollFactor));
        CanvasMap map = CurrentCanvasMap();

        return map.Origin + (canvasPoint * map.Scale) + LayerShift(map, scrollFactor);
    }

    /// <summary>The position in canvas pixels at which <paramref name="worldPoint"/> is drawn.</summary>
    /// <remarks>It answers for the settled framing, as <see cref="CanvasToWorld(Vector2)"/> does.</remarks>
    public Vector2 WorldToCanvas(Vector2 worldPoint) => WorldToCanvas(worldPoint, Vector2.One);

    /// <summary>
    /// The position in canvas pixels at which <paramref name="worldPoint"/> is drawn on the layer of an
    /// entity whose <see cref="Entity.ScrollFactor"/> is <paramref name="scrollFactor"/>.
    /// </summary>
    /// <remarks>It answers for the settled framing, as <see cref="CanvasToWorld(Vector2)"/> does.</remarks>
    public Vector2 WorldToCanvas(Vector2 worldPoint, Vector2 scrollFactor)
    {
        Guard.Finite(worldPoint, nameof(worldPoint));
        Guard.Finite(scrollFactor, nameof(scrollFactor));
        CanvasMap map = CurrentCanvasMap();

        return (worldPoint - LayerShift(map, scrollFactor) - map.Origin) / map.Scale;
    }

    /// <summary>
    /// Cuts to <paramref name="center"/>. The frame this step draws interpolates no centre, zoom or offset.
    /// </summary>
    public void Teleport(Vector2 center)
    {
        Center = center;
        PreviousCenter = center;
        ResetFollow(center);
        _cut = true;
    }

    /// <summary>
    /// Steers this camera's framing for the step, before the follow and the shake settle it. Runs
    /// after every entity and component has stepped, after contacts settle and after the scene's own
    /// <see cref="Scene.OnLateStep"/>, before the step's deferred adds and removes land.
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

    // Draws Bounds and the deadzone on the Camera channel. The visible region is the frame's own edges
    // and says nothing.
    internal void OnDebugDraw()
    {
        if (Bounds is { } bounds)
        {
            DebugDraw.Rect(DebugDraw.Camera, bounds);
        }

        DrawDeadzone();
    }

    internal void SavePrevious()
    {
        PreviousCenter = Center;
        _previousZoom = Zoom;
        _previousOffset = Offset + ShakeOffset;
    }

    // What the renderer draws this camera as, and what the simulation measures visibility against.
    internal CameraView ToView() => new(PreviousCenter, Center, ViewportSize / Zoom, Fit, Bounds, ScrollOrigin)
    {
        PreviousSize = ViewportSize / _previousZoom,
        PreviousOffset = _previousOffset,
        Offset = Offset + ShakeOffset,
    };

    // The engine's half of the late step: the follow, the shake, then the region the frame will use,
    // measured against the step's output. An empty output measures the declared span.
    internal void Settle(in StepContext context)
    {
        StepFollow(context.DeltaSeconds, context.Output);
        StepShake(context.DeltaSeconds);

        if (_cut)
        {
            _cut = false;
            SavePrevious();
        }

        _settled = true;
        (VisibleRegion, _canvasMap) = Frame(context.Output);
    }

    private CanvasMap CurrentCanvasMap() => _canvasMap ?? Frame(Vector2.Zero).Map;

    // A layer at factor f is drawn as if the camera's corner sat at O + (K - O) * f, where O is
    // ScrollOrigin and K the placed region's corner. A canvas point lands (O - K) * (1 - f) away from
    // its world point on that layer.
    private static Vector2 LayerShift(in CanvasMap map, Vector2 scrollFactor) =>
        map.Parallax * (Vector2.One - scrollFactor);

    // The region and canvas map of the frame the host draws on an output of this extent, through the
    // host's own layout. The region is the camera placed at the layout's quantised span. With no output
    // the region is the declared span, and the canvas is letterboxed over it as a surface of the
    // canvas's own size would draw it.
    private (Rect Region, CanvasMap Map) Frame(Vector2 output)
    {
        if (!(ViewportSize.X > 0f) || !(ViewportSize.Y > 0f))
        {
            return (default, CanvasMap.Identity);
        }

        CameraView view = ToView();

        if (HostLayout(view, output) is not { } layout)
        {
            return Declared(view);
        }

        // An output too small to place the world on falls back to the declared letterbox for the map.
        Rect region = view.Place(1f, layout.Span);

        return (region, CanvasMap.Resolve(layout, region, ScrollOrigin) ?? Declared(view).Map);
    }

    // The host's layout of the frame on an output of this extent, or null when the output or the
    // viewport has no area. Its span is quantised to whole surface pixels.
    private ScreenLayout? HostLayout(in CameraView view, Vector2 output)
    {
        int width = (int)output.X;
        int height = (int)output.Y;

        if (width <= 0 || height <= 0 || !(view.Size.X > 0f) || !(view.Size.Y > 0f))
        {
            return null;
        }

        Run? run = _scene?.RunOrNull;

        return FrameLayout.Layout(run?.RenderResolution, view, Canvas, Sampling, width, height);
    }

    // The declared span, with the canvas letterboxed over it.
    private (Rect Region, CanvasMap Map) Declared(in CameraView view)
    {
        Vector2 canvas = Canvas;
        ScreenLayout fitted = FrameLayout.Layout(
            null,
            view with { Fit = ViewportFit.Letterbox },
            canvas,
            Sampling,
            Pixels(canvas.X),
            Pixels(canvas.Y));
        Rect region = view.Place(1f, fitted.Span);

        return (region, CanvasMap.Resolve(fitted, region, ScrollOrigin) ?? CanvasMap.Identity);
    }

    private Vector2 Canvas => _scene?.RunOrNull?.Canvas ?? Run.StandardCanvas;

    private TextureSampling Sampling => _scene?.Sampling ?? TextureSampling.Linear;

    private static int Pixels(float extent) => Math.Max(1, (int)MathF.Round(extent));

    // A canvas point c lands on the world at Origin + c * Scale. Origin is the world point under the
    // canvas's top-left corner, Scale the world units one canvas pixel spans, and Parallax the settled
    // ScrollOrigin less the placed region's top-left corner.
    private readonly record struct CanvasMap(Vector2 Origin, float Scale, Vector2 Parallax)
    {
        internal static CanvasMap Identity => new(Vector2.Zero, 1f, Vector2.Zero);

        // Canvas to surface: a screen layer drawn on the surface lands at OnSurface. Otherwise it lands
        // in the back buffer at Layer, and the surface's present is undone from there. Surface to world:
        // the world's fit on the surface, from the region's corner.
        internal static CanvasMap? Resolve(in ScreenLayout layout, in Rect region, Vector2 scrollOrigin)
        {
            ScreenPlacement onSurface = layout.ScreenOnSurface
                ? layout.OnSurface
                : layout.Present.Scale > 0f
                    ? new ScreenPlacement(
                        (layout.Layer.Origin - layout.Present.Origin) / layout.Present.Scale,
                        layout.Layer.Scale / layout.Present.Scale)
                    : default;
            Letterbox world = layout.World;

            if (!(onSurface.Scale > 0f) || world.IsEmpty || !(world.Scale > 0f))
            {
                return null;
            }

            Vector2 corner = new(region.Left, region.Top);

            return new CanvasMap(
                corner + ((onSurface.Origin - new Vector2(world.X, world.Y)) / world.Scale),
                onSurface.Scale / world.Scale,
                scrollOrigin - corner);
        }
    }

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

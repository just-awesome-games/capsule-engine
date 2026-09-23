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
    /// region the previous step settled. It reads empty before the first late step of the scene this camera
    /// frames, and whenever <see cref="ViewportSize"/> is not positive on both axes.
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

    // The drawing derivation itself, asked at the end of the step against the step's output. An
    // empty output measures the declared span, and every fit resolves to it.
    internal void SettleVisibleRegion(Vector2 output)
    {
        (Rect region, CanvasMap map) = Frame(output);
        VisibleRegion = region;
        _canvasMap = map;
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
        Run? run = _scene?.RunOrNull;
        Vector2 canvas = run?.Canvas ?? Run.StandardCanvas;
        TextureSampling sampling = _scene?.Sampling ?? TextureSampling.Linear;
        int width = (int)output.X;
        int height = (int)output.Y;

        if (width <= 0 || height <= 0)
        {
            return Declared(view, canvas, sampling);
        }

        // An output too small to place the world on falls back to the declared letterbox for the map.
        ScreenLayout layout = FrameLayout.Layout(run?.RenderResolution, view, canvas, sampling, width, height);
        Rect region = view.Place(1f, layout.Span);

        return (region, CanvasMap.Resolve(layout, region, ScrollOrigin) ?? Declared(view, canvas, sampling).Map);
    }

    // The declared span, with the canvas letterboxed over it.
    private (Rect Region, CanvasMap Map) Declared(in CameraView view, Vector2 canvas, TextureSampling sampling)
    {
        ScreenLayout fitted = FrameLayout.Layout(
            null,
            view with { Fit = ViewportFit.Letterbox },
            canvas,
            sampling,
            Pixels(canvas.X),
            Pixels(canvas.Y));
        Rect region = view.Place(1f, fitted.Span);

        return (region, CanvasMap.Resolve(fitted, region, ScrollOrigin) ?? CanvasMap.Identity);
    }

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

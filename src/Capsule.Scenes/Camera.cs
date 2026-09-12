using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// A scene's world-space viewport. Movement interpolates; deliberate cuts use
/// <see cref="Teleport"/>. A non-positive <see cref="ViewportSize"/> draws nothing. A scene
/// installs a subclass to give framing a home of its own: it finds its subject in
/// <see cref="OnStart"/> and settles its framing in <see cref="OnLateStep"/>.
/// </summary>
public class Camera
{
    private bool _started;
    private Scene? _scene;

    /// <summary>The world point the viewport is centred on.</summary>
    public Vector2 Center { get; set; }

    /// <summary><see cref="Center"/> at the previous fixed step, retained by the engine.</summary>
    public Vector2 PreviousCenter { get; internal set; }

    /// <summary>
    /// World units the viewport spans; zero until the scene or its camera sets it, and a
    /// non-positive span draws nothing.
    /// </summary>
    public Vector2 ViewportSize { get; set; }

    /// <summary>
    /// How <see cref="ViewportSize"/> answers an output whose aspect ratio differs from it.
    /// Defaults to <see cref="ViewportFit.Letterbox"/>, which shows that span and nothing else.
    /// </summary>
    public ViewportFit Fit { get; set; }

    /// <summary>
    /// A world rect the visible region may never leave, applied after the fit resolves it: the
    /// region is clamped inside these bounds on each axis, and centred on them along an axis it is
    /// larger than. Null, the default, leaves the view free. <see cref="Center"/> is unaffected —
    /// it stays the raw framing target, and the confinement lives only in what is drawn.
    /// </summary>
    public Rect? Bounds { get; set; }

    /// <summary>
    /// The world rect the frame draws: <see cref="ViewportSize"/> centred on <see cref="Center"/>
    /// and confined to <see cref="Bounds"/> — clamped inside them on each axis, or centred on them
    /// along an axis the viewport is larger than. Engine-owned, and settled once a step immediately
    /// after <see cref="OnLateStep"/>, so an entity or component reading it during its own step
    /// reads the region of the frame last drawn.
    /// <para>
    /// Empty before the first late step of the scene this camera frames — before the camera opens,
    /// and whenever <see cref="ViewportSize"/> is not positive on both axes. This is the settled
    /// framing; the renderer interpolates between the previous step's region and this one, and a
    /// <see cref="Fit"/> other than <see cref="ViewportFit.Letterbox"/> can reveal world past it on
    /// an output whose aspect asks for it, which no output reaches the simulation to say.
    /// </para>
    /// </summary>
    public Rect VisibleRegion { get; private set; }

    /// <summary>
    /// The scene this camera frames; null before <see cref="OnAddedToScene"/> and after
    /// <see cref="OnRemovedFromScene"/>. A camera installed in a scene that has not opened its
    /// camera yet takes the handle when that scene does.
    /// </summary>
    public Scene? Scene
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
    /// Runs once for this camera's lifetime — not again when it is reinstalled — before its first
    /// late step: the scene and every entity it holds have started, so the subject to follow is
    /// found here. A camera installed in a scene that has already opened its camera runs it as it
    /// is installed, unless its own <see cref="OnAddedToScene"/> installs another camera.
    /// </summary>
    protected internal virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once this camera is the scene's, with <see cref="Scene"/> set — never before that
    /// scene and every entity it holds have started, so the scene may be searched from here.
    /// Registration belongs here: it pairs with <see cref="OnRemovedFromScene"/> and runs again on
    /// every reinstall, where <see cref="OnStart"/> runs once for the camera's lifetime.
    /// </summary>
    protected internal virtual void OnAddedToScene()
    {
    }

    /// <summary>
    /// Runs once this camera is no longer the scene's, with <see cref="Scene"/> cleared — when
    /// another camera is installed, and when the scene stops. Anything
    /// <see cref="OnAddedToScene"/> registered is released here.
    /// </summary>
    protected internal virtual void OnRemovedFromScene()
    {
    }

    internal void Retain() => PreviousCenter = Center;

    // The drawing derivation itself, asked at the end of the step and with no output to measure, so
    // the span is the declared one and every fit resolves to it.
    internal void SettleVisibleRegion() =>
        VisibleRegion = new CameraView(PreviousCenter, Center, ViewportSize, Fit, Bounds)
            .Resolve(1f, default);

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

using System.Numerics;

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
    /// The scene this camera frames; null before <see cref="OnAddedToScene"/> and after
    /// <see cref="OnRemovedFromScene"/>. A camera installed in a scene that has not opened its
    /// camera yet takes the handle when that scene does.
    /// </summary>
    public Scene? Scene { get; internal set; }

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

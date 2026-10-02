using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// Runs a <see cref="Scenes.Scene"/> through Capsule's fixed-step lifecycle. A
/// <see cref="SimulationHost"/> or the engine's host steps it.
/// </summary>
public sealed class SceneSimulation : ISimulation, IDisposable
{
    private readonly FrameView _view = new();
    private bool _disposed;
    private bool _warnedOnUndrawnWorldLayer;

    // Whether the scene has changed since View was last built. A new simulation has built nothing.
    private bool _viewStale = true;
    private bool _stepping;
    private bool _building;

    // Whether View was ever built. A disposed simulation returns that frame even when a later step
    // left it stale.
    private bool _viewBuilt;

    private CameraView? _viewCamera;

    /// <summary>Starts <paramref name="scene"/> under <paramref name="run"/>.</summary>
    /// <param name="scene">The scene to run.</param>
    /// <param name="entryPayload">State supplied by the transition that opened the scene.</param>
    /// <param name="run">
    /// The run to install on the scene before it starts. Omit it for a new run with default settings.
    /// </param>
    /// <exception cref="InvalidOperationException">The scene has already been started.</exception>
    /// <exception cref="AggregateException">Starting the scene failed and stopping it then failed too. Both are inner exceptions.</exception>
    public SceneSimulation(
        Scene scene,
        object? entryPayload = null,
        Run? run = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
        Run = run ?? new Run();
        scene.Run = Run;

        // The run boots here, whichever host built it. A scene's start hook already sees a booted run.
        Run.Input.Started = true;
        try
        {
            scene.Start(entryPayload);
        }
        catch (Exception startFailure)
        {
            try
            {
                scene.Stop();
            }
            catch (Exception stopFailure)
            {
                throw new AggregateException(
                    $"Starting and then cleaning up {scene.GetType().Name} both failed.",
                    startFailure,
                    stopFailure);
            }

            throw;
        }
    }

    // A host's stand-in for the scene camera's view, drawn in its place while set. The scene's Camera is
    // never written, and what the simulation reads of it answers for the scene's own framing. Setting or
    // clearing it marks View stale. A new simulation starts with none.
    internal CameraView? ViewCamera
    {
        get => _viewCamera;

        set
        {
            _viewCamera = value;
            _viewStale = true;
        }
    }

    /// <summary>The scene being advanced, for the lifetime of this simulation.</summary>
    public Scene Scene { get; }

    /// <summary>The run installed on <see cref="Scene"/>, shared for this simulation's lifetime.</summary>
    public Run Run { get; }

    /// <summary>Whether the run has asked the host to shut down. Once true, it stays true.</summary>
    public bool ExitRequested => Run.ExitRequested;

    /// <summary>
    /// What to draw, built from the scene on the first read after each step. One instance, and a run
    /// that never reads it never builds it.
    /// </summary>
    /// <remarks>
    /// Building it calls each visible <see cref="Renderer.Draw"/> once. Later reads before the next
    /// step return it unchanged. A disposed simulation keeps the last frame it built.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// It is read during a step, or from inside a <see cref="Renderer.Draw"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This simulation was disposed before it built any frame.
    /// </exception>
    public FrameView View
    {
        get
        {
            ThrowIfMidStepOrDraw();

            if (_disposed)
            {
                if (!_viewBuilt)
                {
                    throw new ObjectDisposedException(
                        nameof(SceneSimulation),
                        "This simulation was disposed before it built any frame. Read View before Dispose to keep a frame.");
                }

                return _view;
            }

            if (_viewStale)
            {
                RewriteView();
            }

            return _view;
        }
    }

    // Advances the scene by exactly one fixed step and marks View for rebuilding. What a callback
    // throws propagates, and a step that throws can leave the state half-changed.
    internal void Step(in StepContext context) => Step(in context, null);

    void ISimulation.Step(in StepContext context) => Step(in context, null);

    // The host's before-step action runs after the mixer's step opens and before the scene's own step.
    void ISimulation.Step(in StepContext context, Action before) => Step(in context, before);

    private void Step(in StepContext context, Action? before)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _stepping = true;
        try
        {
            RunStep(in context, before);
        }
        finally
        {
            _stepping = false;
        }

        // A step that throws leaves a view already built showing the step before it.
        _viewStale = true;
    }

    private void RunStep(in StepContext context, Action? before)
    {
        // Opened first. A sound or rumble pulse played during the step then belongs to this step's tick.
        Run.Audio.BeginStep(in context);
        Run.Rumble.BeginStep(in context);

        before?.Invoke();

        Scene.BeginStep();
        Scene.RunStep(in context);
        Scene.StepEntities(in context);

        // Contacts settle once every position this step produces is final.
        Scene.SettleContacts();

        // Late steps run in step order and read the settled contacts.
        Scene.LateStepEntities(in context);

        // The scene's late step runs before EndStep clears the deferral flag. Its changes then queue too.
        Scene.RunLateStep(in context);
        Scene.EndStep();

        EmitDebugDraws();
    }

    // Runs the debug pass over the scene as it stands, after each step and when a host redraws settled
    // state. Skipped while nothing listens and on a run a host owns for its own overlay.
    internal void EmitDebugDraws()
    {
        if (DebugDraw.IsAttached && Run.EmitsDebugDraw)
        {
            Scene.RunDebugDraw();
        }
    }

    // The host takes the request from the frame that will serve it.
    internal bool TryTakeFrameCapture(out string path) => Run.TryTakeFrameCapture(out path);

    /// <summary>Takes the deferred transition the last step requested, when there is one.</summary>
    /// <exception cref="ObjectDisposedException">This simulation has been disposed.</exception>
    public bool TryTakeTransition(out SceneTransition transition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Run.TryTakeTransition(out transition);
    }

    /// <summary>Stops the scene and releases every entity it holds.</summary>
    /// <exception cref="AggregateException">More than one stop hook failed. Each failure is an inner exception.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Scene.Stop();
    }

    // Rebuilds View from the scene as it stands, without taking a step.
    internal void RewriteView()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfMidStepOrDraw();

        _building = true;
        try
        {
            Scene.DrawFrame(_view, _viewCamera);
        }
        finally
        {
            _building = false;
        }

        _viewStale = false;
        _viewBuilt = true;
        WarnOnUndrawnWorldLayer();
    }

    private void ThrowIfMidStepOrDraw()
    {
        if (_building)
        {
            throw new InvalidOperationException(
                "SceneSimulation.View was read from inside a Renderer.Draw while that view was being built. Write to the FrameView the Draw is handed instead.");
        }

        if (_stepping)
        {
            throw new InvalidOperationException(
                "SceneSimulation.View was read during a step. The view shows a completed step. Read it after Step returns, and read the scene's own state inside a step.");
        }
    }

    // Reads what the frame built, not whether a camera was configured. A screen-only scene never
    // touches the camera and must not warn. The warning fires once per simulation.
    private void WarnOnUndrawnWorldLayer()
    {
        if (_warnedOnUndrawnWorldLayer)
        {
            return;
        }

        if (_view.Sprites.Length == 0 && _view.Lines.Length == 0)
        {
            return;
        }

        Vector2 viewport = Scene.Camera.ViewportSize;
        if (viewport.X > 0f && viewport.Y > 0f)
        {
            return;
        }

        _warnedOnUndrawnWorldLayer = true;
        Log.Warning(
            "the world layer has content but the scene's Camera.ViewportSize is not positive on both "
            + "axes. The scene renders black. Set the scene's camera to a positive ViewportSize");
    }
}

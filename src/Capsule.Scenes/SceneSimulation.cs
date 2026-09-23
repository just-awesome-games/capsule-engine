using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>Runs a <see cref="Scenes.Scene"/> through Capsule's fixed-step lifecycle.</summary>
public sealed class SceneSimulation : ISimulation, IDisposable
{
    private readonly FrameView _view = new();
    private bool _disposed;
    private bool _warnedOnUndrawnWorldLayer;

    /// <summary>Starts <paramref name="scene"/> under <paramref name="run"/> and builds its first frame.</summary>
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

        // The run boots here, whichever host built it. A scene's start hook already sees a booted run,
        // so the debug-menu button is fixed before it can be reached.
        Run.Input.Started = true;
        try
        {
            scene.Start(entryPayload);
            RewriteView();
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

    /// <summary>The scene being advanced, for the lifetime of this simulation.</summary>
    public Scene Scene { get; }

    /// <summary>The run installed on <see cref="Scene"/>, shared for this simulation's lifetime.</summary>
    public Run Run { get; }

    /// <summary>Whether the run has asked the host to shut down. Once true, it stays true.</summary>
    public bool ExitRequested => Run.ExitRequested;

    /// <summary>What to draw. One instance, filled at construction and rewritten after each completed step.</summary>
    public FrameView View => _view;

    /// <summary>
    /// Advances the scene by exactly one fixed step and rebuilds <see cref="View"/>. Exceptions from
    /// scene, entity, component, contact, camera or renderer callbacks propagate to the caller. A step
    /// that throws may have already changed simulation state, so do not continue that simulation.
    /// </summary>
    public void Step(in StepContext context) => Step(in context, null);

    // The host's before-step action runs after the mixer's step opens, so the sounds it plays and stops
    // belong to this step, and before the scene's own step, so the step reads its changes.
    void ISimulation.Step(in StepContext context, Action before) => Step(in context, before);

    private void Step(in StepContext context, Action? before)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Runs before everything else in the step. A sound or a rumble pulse played during it expires
        // against this step's tick, and the sound's commands belong to this step instead of the
        // previous one.
        Run.Audio.BeginStep(in context);
        Run.Rumble.BeginStep(in context);

        before?.Invoke();

        Scene.BeginStep();
        Scene.RunStep(in context);
        Scene.StepEntities(in context);

        // Contacts settle after every position this step produces is final, so no enter or exit is raised
        // against a position something is about to leave.
        Scene.SettleContacts();

        // Then the late steps, in the order the step ran. The next frame shows whatever an entity
        // reads here, such as a position a sweep came to rest at or health a contact just spent.
        Scene.LateStepEntities(in context);

        // Runs before EndStep, because EndStep clears the deferral flag and a late step after it would
        // reach the entity list directly instead of queueing like everything else.
        Scene.RunLateStep(in context);
        Scene.EndStep();

        // Everything the step produced is settled here, and nothing this pass reads changes before the
        // frame is drawn.
        EmitDebugDraws();

        RewriteView();
    }

    // Runs the debug pass over the scene as it stands. Every step calls it once the step has settled, and
    // a host calls it to redraw settled state while paused or after attaching a listener. The hooks are
    // read-only by contract, and an out-of-step pass changes nothing and advances no tick. Skipped
    // while nothing listens, and on a run a host owns for its own overlay.
    internal void EmitDebugDraws()
    {
        if (DebugDraw.IsAttached && Run.EmitsDebugDraw)
        {
            Scene.RunDebugDraw();
        }
    }

    // Takes the run's pending frame capture request and clears it. Not tied to a step. The host calls
    // it from the frame that will serve the request. Run.FrameCaptureRequested reads it without taking
    // it.
    internal bool TryTakeFrameCapture(out string path) => Run.TryTakeFrameCapture(out path);

    /// <summary>Takes the deferred transition the last step requested, when there is one.</summary>
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

    // Rebuilds View from the scene as it stands, without taking a step. A host calls it when its
    // renderers read something that changed between steps.
    internal void RewriteView()
    {
        _view.Clear();
        _view.Camera = Scene.Camera.ToView();
        _view.Canvas = Run.Canvas;
        _view.ClearColor = Scene.ClearColor;
        _view.Ambient = Scene.Ambient;
        _view.Sampling = Scene.Sampling;

        // Drawing runs after EndStep. A key written, a renderer detached or an entity removed from
        // inside Draw reaches the scene directly instead of queueing. The list is frozen for the
        // traversal and walked once by index, in the order the frame opened with. Each renderer is
        // checked against the scene before it draws, so one detached or removed by an earlier Draw is
        // skipped. Anything invalidated rebuilds at EndDraw, and a renderer attached here first draws
        // next step.
        Scene.BeginDraw();
        try
        {
            ReadOnlySpan<Renderer> renderers = Scene.RenderersInDrawOrder();
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!Scene.Draws(renderer) || !renderer.Visible)
                {
                    continue;
                }

                // The only place the render space, scroll factor and tint are chosen. A renderer follows its
                // entity. A hidden or fully faded entity's renderers are skipped before Draw.
                Entity entity = renderer.Entity!;
                if (entity.TryGetDrawTint(out ColorRgba tint))
                {
                    _view.Space = entity.Space;
                    _view.ScrollFactor = entity.ScrollFactor;
                    _view.Tint = tint;
                    renderer.Draw(_view);
                }
            }
        }
        finally
        {
            _view.Space = RenderSpace.World;
            _view.ScrollFactor = Vector2.One;
            _view.Tint = ColorRgba.White;
            Scene.EndDraw();
        }

        WarnOnUndrawnWorldLayer();
    }

    // The world layer can have content and still draw nothing, because the camera has no positive
    // span. A screen-only scene such as a boot menu never touches the camera and must not warn, so
    // this reads what the frame just built rather than whether a camera was configured. Latched to
    // the instance so a scene that never fixes it hears about it once, not every frame.
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

using Capsule.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>Runs a <see cref="Scenes.Scene"/> through Capsule's fixed-step lifecycle.</summary>
public sealed class SceneSimulation : ISimulation, IDisposable
{
    private readonly FrameView _view = new();
    private bool _disposed;

    /// <summary>Starts <paramref name="scene"/> under <paramref name="run"/> and builds its first frame.</summary>
    /// <param name="scene">The scene to run.</param>
    /// <param name="entryPayload">State supplied by the transition that opened the scene.</param>
    /// <param name="run">
    /// The run to install on the scene before it starts; omitted, a new run with default settings.
    /// </param>
    /// <exception cref="InvalidOperationException">The scene has already been started.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    /// <exception cref="AggregateException">Starting the scene failed and stopping it then failed too; both are inner exceptions.</exception>
    public SceneSimulation(
        Scene scene,
        object? entryPayload = null,
        Run? run = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
        Run = run ?? new Run();
        scene.Run = Run;
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

    /// <summary>Whether the run has asked the host to shut down; never cleared.</summary>
    public bool ExitRequested => Run.ExitRequested;

    /// <summary>What to draw: one held instance, populated at construction and rewritten after each completed step.</summary>
    public FrameView View => _view;

    /// <summary>
    /// Advances the scene by exactly one fixed step and rebuilds <see cref="View"/>. Exceptions
    /// from scene, entity, component, contact, camera or renderer callbacks propagate to the
    /// caller. A step that throws may have changed simulation state; continuing that simulation is
    /// not supported.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The simulation has been disposed.</exception>
    public void Step(in StepContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Ahead of everything the step runs: a sound played during it expires against this step's
        // tick, and the commands it raises are this step's rather than the previous one's.
        Run.Audio.BeginStep(in context);

        Scene.BeginStep();
        Scene.RunStep(in context);
        Scene.StepEntities(in context);

        // Contacts settle where every position this step will produce has been produced, so an
        // enter or exit is never raised against a position something is about to leave.
        Scene.SettleContacts();

        // Then the late steps, in the order the step ran: everything an entity reads here — a
        // position a sweep came to rest at, health a contact just spent — is what the frame about to
        // be drawn will show.
        Scene.LateStepEntities(in context);

        // Ahead of EndStep, not after it: EndStep clears the deferral flag, so a late step run
        // past it would reach the entity list directly instead of queueing like everything else.
        Scene.RunLateStep(in context);
        Scene.EndStep();

        // Everything the step left is settled here, and nothing the pass reads can change before
        // the frame is drawn. Skipped outright while nothing listens, and on a run a host owns for
        // its own overlay: the walk costs the same whether or not anything hears it.
        if (DebugDraw.IsAttached && Run.EmitsDebugDraw)
        {
            Scene.RunDebugDraw();
        }

        RewriteView();
    }

    // Step with the host's before-step act inside it, run once the mixer's step has opened so the
    // sounds it plays and stops are this step's commands, and before the scene's own step so its
    // mutations are what the step then reads. The same step as Step, spelt out again rather than
    // shared, so the ordinary step carries no branch for an act it never has.
    void ISimulation.Step(in StepContext context, Action before)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Run.Audio.BeginStep(in context);

        before();

        Scene.BeginStep();
        Scene.RunStep(in context);
        Scene.StepEntities(in context);
        Scene.SettleContacts();
        Scene.LateStepEntities(in context);
        Scene.RunLateStep(in context);
        Scene.EndStep();

        if (DebugDraw.IsAttached && Run.EmitsDebugDraw)
        {
            Scene.RunDebugDraw();
        }

        RewriteView();
    }

    // Takes the run's pending frame capture request, clearing it. Not step-bound: the host calls it
    // from the frame that will serve it. Readable without taking as Run.FrameCaptureRequested.
    internal bool TryTakeFrameCapture(out string path) => Run.TryTakeFrameCapture(out path);

    /// <summary>Takes the deferred transition requested by the last step, if one was requested.</summary>
    /// <exception cref="ObjectDisposedException">The instance has been disposed.</exception>
    public bool TryTakeTransition(out SceneTransition transition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Run.TryTakeTransition(out transition);
    }

    /// <summary>Stops the scene and releases every entity it holds.</summary>
    /// <exception cref="Exception">One stop hook failed; teardown still completed for every entity.</exception>
    /// <exception cref="AggregateException">More than one stop hook failed; each is an inner exception.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Scene.Stop();
    }

    // Rebuilds View from the scene as it stands, without a step: what a step ends with, for a host
    // whose renderers read something that changed between steps.
    internal void RewriteView()
    {
        _view.Clear();
        _view.Camera = new CameraView(
            Scene.Camera.PreviousCenter,
            Scene.Camera.Center,
            Scene.Camera.ViewportSize,
            Scene.Camera.Fit,
            Scene.Camera.Bounds);
        _view.Canvas = Run.Canvas;
        _view.ClearColor = Scene.ClearColor;
        _view.Sampling = Scene.Sampling;

        // Drawing runs past EndStep, so a Draw that writes a key, detaches a renderer or removes an
        // entity reaches the scene directly rather than queueing. The list is frozen for the length
        // of the traversal and walked once by index: every renderer it holds is offered exactly
        // once, in the order the frame opened with, and each is checked against the scene before it
        // draws so one detached or removed by an earlier Draw is skipped. Whatever was invalidated
        // rebuilds at EndDraw, which is why a renderer attached here first draws next step.
        Scene.BeginDraw();
        try
        {
            ReadOnlySpan<Renderer> renderers = Scene.RenderersInDrawOrder();
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (Scene.Draws(renderer))
                {
                    // The one place a space is chosen: a renderer follows its entity.
                    _view.Space = renderer.Entity!.Space;
                    renderer.Draw(_view);
                }
            }
        }
        finally
        {
            _view.Space = RenderSpace.World;
            Scene.EndDraw();
        }
    }
}

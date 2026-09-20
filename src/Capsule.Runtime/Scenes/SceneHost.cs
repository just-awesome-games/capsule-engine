using Capsule.Assets;
using Capsule.Persistence;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime.Scenes;

internal delegate Scene SceneResolver(in SceneTransition target);

// Keeps the runtime alive while scenes replace one another. Each requested target is resolved at the
// host boundary, so content never enters game logic as a file operation.
internal sealed class SceneHost : ISimulation, IDisposable
{
    private readonly SceneResolver _resolve;
    private readonly Run _run;

    // Null for a run with no medium behind its saves, which keeps them in memory and flushes nothing.
    private readonly ISaveStorage? _saveStorage;

    private SceneTransition _target;
    private SceneSimulation _current;
    private bool _disposed;

    internal SceneHost(
        in SceneTransition initialTarget,
        SceneResolver resolve,
        Run run,
        ISaveStorage? saveStorage = null,
        Action<Run>? onRunStart = null)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(run);

        _resolve = resolve;
        _run = run;
        _target = initialTarget;

        // Restored ahead of the first scene, whose start may read its settings.
        _saveStorage = saveStorage;
        if (saveStorage is not null)
        {
            run.Saves.Restore(saveStorage);
        }

        onRunStart?.Invoke(run);

        _current = new SceneSimulation(resolve(initialTarget), initialTarget.Payload, _run);
    }

    public bool ExitRequested { get; private set; }

    public FrameView View => _current.View;

    internal Scene Scene => _current.Scene;

    internal Run Run => _run;

    // Null until the device is ready. Later transitions prepare their incoming scene through it.
    internal Action<AssetCollection>? PrepareAssets { get; set; }

    // Whether the last step's transition failed to bring its incoming scene up, because resolving,
    // preparing or starting it threw. The run stays on the scene it was on and steps as before. The
    // exception still propagated, and this reports where it came from. A step's own failure, after
    // which the simulation does not continue, is not reported here.
    internal bool TransitionFailed { get; private set; }

    public void Step(in StepContext context)
    {
        if (!CanStep())
        {
            return;
        }

        _current.Step(context);
        Consume();
    }

    // Step with the host's before-step act inside the current scene's step. A scene the act asks for
    // becomes the transition this step consumes.
    void ISimulation.Step(in StepContext context, Action before)
    {
        if (!CanStep())
        {
            return;
        }

        ((ISimulation)_current).Step(context, before);
        Consume();
    }

    // Opens a step and returns whether there is one to run. An exit already tore the current scene
    // down, and a disposed host cannot be stepped.
    private bool CanStep()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TransitionFailed = false;

        return !ExitRequested;
    }

    // The transition the step just taken asked for, resolved at the host boundary.
    private void Consume()
    {
        if (!_current.TryTakeTransition(out SceneTransition transition))
        {
            return;
        }

        switch (transition.Kind)
        {
            case SceneTransitionKind.Exit:
                ExitRequested = true;
                try
                {
                    _current.Dispose();
                }
                finally
                {
                    ReleaseAssets();
                }

                break;

            case SceneTransitionKind.Restart:
                Replace(transition.HasPayload ? _target.WithPayload(transition.Payload) : _target);
                break;

            case SceneTransitionKind.Scene:
            case SceneTransitionKind.Named:
                Replace(transition);
                break;

            default:
                throw new InvalidOperationException($"Unknown scene transition kind '{transition.Kind}'.");
        }
    }

    // Runs the current scene's debug pass outside a step, as SceneSimulation.EmitDebugDraws defines
    // it. Nothing runs after an exit, which has nothing left to draw.
    internal void EmitDebugDraws()
    {
        if (!ExitRequested)
        {
            _current.EmitDebugDraws();
        }
    }

    // Takes the run's pending frame capture request. A transition builds a new scene without
    // discarding the request, and an exit leaves nothing to serve.
    internal bool TryTakeFrameCapture(out string path)
    {
        if (ExitRequested)
        {
            path = "";
            return false;
        }

        return _current.TryTakeFrameCapture(out path);
    }

    // Called after each step, which persists a document written in the step that requests exit, and
    // again at disposal for what a scene's stop wrote on a window closed from outside the run. The
    // clock is read here, because the store in Core reads none.
    internal void FlushSaves()
    {
        if (_saveStorage is { } storage)
        {
            _run.Saves.Flush(storage, DateTimeOffset.Now);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _current.Dispose();
        }
        finally
        {
            try
            {
                ReleaseAssets();
            }
            finally
            {
                FlushSaves();
            }
        }
    }

    private void Replace(in SceneTransition target)
    {
        SceneSimulation incoming;
        try
        {
            incoming = Bring(in target);
        }
        catch
        {
            TransitionFailed = true;
            throw;
        }

        try
        {
            _current.Dispose();
        }
        catch (Exception disposeFailure)
        {
            try
            {
                incoming.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    $"Stopping {_current.Scene.GetType().Name} and then releasing {incoming.Scene.GetType().Name} both failed.",
                    disposeFailure,
                    cleanupFailure);
            }

            throw;
        }

        _current = incoming;
        _target = target;
    }

    // Resolves, prepares and starts the incoming scene ahead of the outgoing one's teardown, so a
    // failure here leaves the run on the scene it was on. A scene that fails to start was stopped by
    // its simulation.
    private SceneSimulation Bring(in SceneTransition target)
    {
        Scene next = _resolve(target);

        try
        {
            PrepareAssets?.Invoke(next.CollectAssetPreloads());
        }
        catch (Exception preparationFailure)
        {
            try
            {
                next.Abandon();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    $"Preparing {next.GetType().Name}'s assets and then releasing it both failed.",
                    preparationFailure,
                    cleanupFailure);
            }

            throw;
        }

        return new SceneSimulation(next, target.Payload, _run);
    }

    private void ReleaseAssets()
    {
        Action<AssetCollection>? prepare = PrepareAssets;
        PrepareAssets = null;
        prepare?.Invoke(new AssetCollection());
    }
}

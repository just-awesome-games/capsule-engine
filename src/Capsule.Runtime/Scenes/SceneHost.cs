using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime.Scenes;

internal delegate Scene SceneResolver(in SceneTransition target);

// Keeps the runtime alive while scenes replace one another, resolving each requested target at the
// host boundary so content never enters game logic as a file operation.
internal sealed class SceneHost : ISimulation, IDisposable
{
    private readonly SceneResolver _resolve;
    private readonly Run _run;

    private SceneTransition _target;
    private SceneSimulation _current;
    private bool _disposed;

    internal SceneHost(in SceneTransition initialTarget, SceneResolver resolve, Run run)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(run);

        _resolve = resolve;
        _run = run;
        _target = initialTarget;
        _current = new SceneSimulation(resolve(initialTarget), initialTarget.Payload, _run);
    }

    public bool ExitRequested { get; private set; }

    public FrameView View => _current.View;

    internal Scene Scene => _current.Scene;

    internal Run Run => _run;

    // Null until the device is ready. Later transitions prepare their incoming scene through it.
    internal Action<AssetCollection>? PrepareAssets { get; set; }

    public void Step(in StepContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The exit already tore the current scene down; there is nothing left to step.
        if (ExitRequested)
        {
            return;
        }

        _current.Step(context);
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

    // Takes the run's pending frame capture request. A transition builds a new scene without
    // discarding it; an exit leaves nothing to serve.
    internal bool TryTakeFrameCapture(out string path)
    {
        if (ExitRequested)
        {
            path = "";
            return false;
        }

        return _current.TryTakeFrameCapture(out path);
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
            ReleaseAssets();
        }
    }

    private void Replace(in SceneTransition target)
    {
        Scene next = _resolve(target);

        try
        {
            // Before the outgoing scene is torn down: a preload that fails must leave the run on
            // the scene it was on.
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

        _current.Dispose();
        _current = new SceneSimulation(next, target.Payload, _run);
        _target = target;
    }

    private void ReleaseAssets()
    {
        Action<AssetCollection>? prepare = PrepareAssets;
        PrepareAssets = null;
        prepare?.Invoke(new AssetCollection());
    }
}

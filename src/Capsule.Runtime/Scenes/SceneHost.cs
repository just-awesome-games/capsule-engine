using Capsule.Assets;
using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Persistence;
using Capsule.Rendering;
using Capsule.Runtime.Audio;
using Capsule.Scenes;

namespace Capsule.Runtime.Scenes;

internal delegate Scene SceneResolver(in SceneTransition target);

// Keeps the runtime alive while scenes replace one another. Each requested target is resolved at the
// host boundary. Content never enters game logic as a file operation.
internal sealed class SceneHost : ISimulation, IDisposable
{
    private readonly SceneResolver _resolve;

    // Null for a run with no medium behind its saves, which keeps them in memory and flushes nothing.
    private readonly ISaveStorage? _saveStorage;

    private readonly SceneRegistry? _registry;
    private readonly Action<Run>? _onRunEnd;
    private SceneTransition _target;
    private SceneSimulation _current;

    // The prefetch no boundary has consumed yet, which a repeat of it leaves alone.
    private SceneTransition? _prefetched;

    // The initial scene's collection, taken before it started and held until a device can load it.
    private AssetCollection? _initialPreloads;
    private bool _ended;
    private bool _disposed;

    public bool ExitRequested { get; private set; }

    internal Run Run { get; }

    // Null until the device is ready. Later transitions prepare their incoming scene through it.
    internal Action<AssetCollection>? PrepareAssets { get; set; }

    // Null until the device is ready, and for a run with none.
    internal Action<AssetCollection>? PrefetchAssets { get; set; }

    // Runs once a step's transition is taken, before the boundary exchanges or releases the scene's
    // assets. What the outgoing scene raised is delivered here, against the assets it declared. Null
    // for a run without a device.
    internal Action? BoundaryReached { get; set; }

    // Runs once a transition's incoming scene has started and the outgoing one is gone, before the incoming
    // scene steps or draws. Null for a run without a device.
    internal Action? SceneEntered { get; set; }

    // Whether the last step's transition failed to bring its incoming scene up, because resolving,
    // preparing or starting it threw. The run stays on the scene it was on and steps as before. The
    // exception still propagated, and this reports where it came from. A step's own failure, after
    // which the simulation does not continue, is not reported here.
    internal bool TransitionFailed { get; private set; }

    // The current scene's key, as --scene takes it, which the overlay, a recording's file name and a trace show.
    internal string SceneKey { get; private set; }

    internal SceneHost(
        in SceneTransition initialTarget,
        SceneResolver resolve,
        Run run,
        ISaveStorage? saveStorage = null,
        Action<Run>? onRunStart = null,
        SceneRegistry? registry = null,
        Action<Run>? onRunEnd = null)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(run);

        _resolve = resolve;
        _registry = registry;
        _onRunEnd = onRunEnd;
        Run = run;
        _target = initialTarget;
        SceneKey = KeyOf(initialTarget);

        // Restored ahead of the first scene, whose start may read its settings.
        _saveStorage = saveStorage;
        if (saveStorage is not null)
        {
            run.Saves.Restore(saveStorage);
        }

        onRunStart?.Invoke(run);

        // Collected before the start, as a transition collects its incoming scene.
        Scene initial = resolve(initialTarget);
        _initialPreloads = Prepare(initial, initialTarget.Payload, prepare: null);
        _current = new SceneSimulation(initial, initialTarget.Payload, run);
    }

    public FrameView View => _current.View;

    internal Scene Scene => _current.Scene;

    // The current simulation's stand-in camera. A scene the run moves to starts with none.
    internal CameraView? ViewCamera
    {
        get => _current.ViewCamera;
        set => _current.ViewCamera = value;
    }

    // The current simulation's draw attribution. A scene the run moves to starts with none.
    internal List<DrawRun>? DrawRuns
    {
        get => _current.DrawRuns;
        set => _current.DrawRuns = value;
    }

    public void Step(in StepContext context)
    {
        if (!CanStep())
        {
            return;
        }

        _current.Step(context);
        Consume();
        TakePrefetch();
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
        TakePrefetch();
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

        BoundaryReached?.Invoke();

        switch (transition.Kind)
        {
            case SceneTransitionKind.Exit:
                ExitRequested = true;
                try
                {
                    // Built before the scene stops. The run's last frame stays readable after it.
                    _ = _current.View;
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

    // Loads the initial scene's pre-start collection through PrepareAssets, once. The initial scene
    // started before the device existed.
    internal void PrepareInitialAssets()
    {
        AssetCollection? preloads = _initialPreloads;
        _initialPreloads = null;
        if (preloads is not null)
        {
            PrepareAssets?.Invoke(preloads);
        }
    }

    // The target is built, collected with its payload and abandoned in every mode. A run with a device and
    // one without construct the same scenes. The same scene under another payload is another prefetch.
    private void TakePrefetch()
    {
        if (ExitRequested || !Run.TryTakePrefetch(out SceneTransition target) || _prefetched == target)
        {
            return;
        }

        Scene throwaway = _resolve(target);
        AssetCollection preloads;
        try
        {
            preloads = throwaway.CollectAssetPreloads(target.Payload);
        }
        finally
        {
            throwaway.Abandon();
        }

        if (_prefetched is { } replaced)
        {
            Log.Debug($"prefetch of {NameOf(replaced)} replaced by {NameOf(target)} before use");
        }

        _prefetched = target;
        PrefetchAssets?.Invoke(preloads);
    }

    private static string NameOf(in SceneTransition target) => target.SceneType?.Name ?? target.DocumentName ?? "";

    private string KeyOf(in SceneTransition target) => _registry?.KeyOf(in target) ?? NameOf(in target);

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

    // Delivers each step's sound and saves per step, because the mixer rewrites its commands every
    // step and a frame may run several. Each command is applied once: one raised before a scene
    // boundary ahead of it, against the stores the outgoing scene declared, and the rest after the
    // step, which carries what an incoming scene's start raised. The flush after the step that
    // requests exit persists that step's writes.
    internal void FollowSteps(FixedStepScheduler scheduler, AudioPlayer? audio)
    {
        if (audio is null)
        {
            scheduler.StepCompleted = FlushSaves;

            return;
        }

        AudioMixer mixer = Run.Audio;
        BoundaryReached = () => audio.Apply(mixer.TakeCommands());
        scheduler.StepCompleted = () =>
        {
            audio.Apply(mixer.TakeCommands());
            FlushSaves();
        };

        // The initial scene started before the device existed. What its start raised is still on
        // the mixer and the first BeginStep would clear it unheard.
        audio.Apply(mixer.TakeCommands());
    }

    // Called after each step, which persists a document written in the step that requests exit, and
    // again at disposal for what a scene's stop wrote on a window closed from outside the run. The
    // clock is read here, because the store in Core reads none, and only when a document will land.
    internal void FlushSaves()
    {
        if (_saveStorage is { } storage && Run.Saves.IsDirty)
        {
            Run.Saves.Flush(storage, DateTimeOffset.Now);
        }
    }

    // Ends a run that did not crash, once, after its last step: the last scene stops unless an exit
    // already stopped it, then the run-end hook runs. Dispose then persists what either wrote. A host
    // calls it when its loop returns, and a crash unwinds past it to Dispose alone.
    internal void End()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ended)
        {
            return;
        }

        _ended = true;
        _current.Dispose();
        _onRunEnd?.Invoke(Run);
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
            Failure.CleanUp(
                disposeFailure,
                incoming.Dispose,
                $"Stopping {_current.Scene.GetType().Name} and then releasing {incoming.Scene.GetType().Name} both failed.");
            throw;
        }

        _current = incoming;
        _target = target;
        SceneKey = KeyOf(target);
        SceneEntered?.Invoke();
    }

    // Resolves, prepares and starts the incoming scene ahead of the outgoing one's teardown. A
    // failure here leaves the run on the scene it was on. A scene that fails to start was stopped by
    // its simulation.
    private SceneSimulation Bring(in SceneTransition target)
    {
        // The boundary consumes any prefetch, whether or not it named this scene or its payload, and collects
        // the scene with the payload it opens with.
        _prefetched = null;
        Scene next = _resolve(target);
        Prepare(next, target.Payload, PrepareAssets);

        return new SceneSimulation(next, target.Payload, Run);
    }

    // Collects a scene's preloads with the payload it opens with under every host and hands them to prepare,
    // which only a host with a device sets. A failure releases the scene, which never started.
    private static AssetCollection Prepare(Scene scene, IScenePayload? payload, Action<AssetCollection>? prepare)
    {
        try
        {
            AssetCollection preloads = scene.CollectAssetPreloads(payload);
            prepare?.Invoke(preloads);

            return preloads;
        }
        catch (Exception failure)
        {
            Failure.CleanUp(failure, scene.Abandon, $"Preparing {scene.GetType().Name}'s assets and then releasing it both failed.");
            throw;
        }
    }

    private void ReleaseAssets()
    {
        Action<AssetCollection>? prepare = PrepareAssets;
        PrepareAssets = null;
        PrefetchAssets = null;
        prepare?.Invoke(new AssetCollection());
    }
}

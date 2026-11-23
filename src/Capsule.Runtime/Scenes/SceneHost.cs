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

    // The composer behind the resolver, which reads a prefetch's document ahead. Null for a host whose
    // resolver reads nothing ahead.
    private readonly SceneComposer? _composer;

    // Null for a run with no medium behind its saves, which keeps them in memory and flushes nothing.
    private readonly ISaveStorage? _saveStorage;

    private readonly SceneRegistry? _registry;

    // Lends each scene the host runs the view the scene before it returned, which keeps the capacity the
    // busiest of them grew.
    private readonly ViewLender _views = new();

    private SceneTransition _target;
    private SceneSimulation _current;

    // The prefetch no boundary has consumed yet, which a repeat of it leaves alone.
    private SceneTransition? _prefetched;

    // The prefetch whose document is being read, which the end of the step at _buildsAt builds.
    private SceneTransition? _reading;
    private long _buildsAt;

    // The simulated time a prefetch's document read is given before its scene is built. A 10 MB document parses in
    // about 35 ms on a fast desktop, and this leaves room for a machine a few times slower.
    private const double ReadLead = 0.1;

    // The prefetch's scene, built and collected but never started, which the boundary naming the same target
    // starts and any other boundary abandons.
    private PrefetchedScene? _built;

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
        SceneComposer? composer = null)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(run);

        _resolve = resolve;
        _composer = composer;
        _registry = registry;
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
        _initialPreloads = CollectPreloads(initial, initialTarget.Payload);
        _current = new SceneSimulation(initial, initialTarget.Payload, run, _views);
        composer?.Entered(initial.DocumentKey);
    }

    public FrameView View => _current.View;

    // Whether the host keeps what the scenes' views drew on the device from frame to frame, set before the first is drawn.
    internal bool RetainsDrawState
    {
        get => _views.RetainsDrawState;
        set => _views.RetainsDrawState = value;
    }

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
        Finish(context);
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
        Finish(context);
    }

    // Scene entry runs after the replacement returns so the boundary collection can reclaim the outgoing
    // scene. The incoming scene's prefetch waits until the next step so the host builds at most one scene per step.
    private void Finish(in StepContext context)
    {
        if (!Consume())
        {
            TakePrefetch(context);
        }
        else if (!ExitRequested)
        {
            SceneEntered?.Invoke();
        }
    }

    // Opens a step and returns whether there is one to run. An exit already tore the current scene
    // down, and a disposed host cannot be stepped.
    private bool CanStep()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TransitionFailed = false;

        return !ExitRequested;
    }

    // Returns whether the step requested a transition and resolves it at the host boundary.
    private bool Consume()
    {
        if (!_current.TryTakeTransition(out SceneTransition transition))
        {
            return false;
        }

        BoundaryReached?.Invoke();

        switch (transition.Kind)
        {
            case SceneTransitionKind.Exit:
                ExitRequested = true;
                try
                {
                    DropPrefetch();

                    // The run's last frame stays readable after the scene stops.
                    _current.DisposeKeepingLastFrame();
                }
                finally
                {
                    ReleaseAssets();
                }

                break;

            case SceneTransitionKind.Restart:
                AbandonBuilt();
                Replace(transition.HasPayload ? _target.WithPayload(transition.Payload) : _target);
                break;

            case SceneTransitionKind.Scene:
            case SceneTransitionKind.Named:
                Replace(transition);
                break;

            default:
                throw new InvalidOperationException($"Unknown scene transition kind '{transition.Kind}'.");
        }

        return true;
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

    // Builds the prefetch whose document has had its lead, or takes a new one. The scene is built and collected
    // with its payload in every mode and held until a boundary. A run with a device and one without construct the
    // same scenes. One whose document is read first is built at the end of the step ReadLead of simulated time
    // later, which waits for any read still running. A request before then builds it on the request's step. The
    // step it is built on never depends on the read. A prefetch of another scene or payload abandons the held one.
    private void TakePrefetch(in StepContext context)
    {
        if (ExitRequested)
        {
            return;
        }

        bool asked = Run.TryTakePrefetch(out SceneTransition target) && !(_prefetched is { } current && Opens(current, target));
        if (!asked)
        {
            if (_reading is { } read && context.Tick >= _buildsAt)
            {
                _reading = null;
                Build(read);
            }

            return;
        }

        if (_prefetched is { } replaced)
        {
            Log.Debug($"prefetch of {NameOf(replaced)} replaced by {NameOf(target)} before use");
            AbandonBuilt();
        }

        _prefetched = target;
        _reading = _composer?.BeginRead(target) == true ? target : null;
        _buildsAt = context.Tick + Math.Max(1L, (long)Math.Round(ReadLead / context.DeltaSeconds, MidpointRounding.AwayFromZero));
        if (_reading is null)
        {
            Build(target);
        }
    }

    // Builds and collects the prefetch's scene, and holds it.
    private void Build(in SceneTransition target)
    {
        Scene scene = _resolve(target);
        AssetCollection preloads = CollectPreloads(scene, target.Payload);
        Prepare(scene, preloads, PrefetchAssets);
        _built = new PrefetchedScene(target, scene, preloads);
    }

    // Ends the prefetch's read and abandons its scene, for a host that will open no scene again.
    private void DropPrefetch()
    {
        _reading = null;
        _composer?.EndRead();
        AbandonBuilt();
    }

    // Releases the held prefetch's scene, which never started, through the path a failed transition takes.
    private void AbandonBuilt()
    {
        if (_built is { } built)
        {
            _built = null;
            built.Scene.Abandon();
        }
    }

    // Whether two targets open the same scene with equal payloads, whether each names its class or its document.
    private bool Opens(in SceneTransition target, in SceneTransition other) =>
        Equals(target.Payload, other.Payload) && IdentityOf(target) == IdentityOf(other);

    private (Type? Scene, string? Document) IdentityOf(in SceneTransition target) =>
        _registry?.IdentityOf(in target) ?? (target.SceneType, target.DocumentName);

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
        // the mixer and the first AudioMixer.BeginStep would clear it unheard.
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
    // already stopped it, then the game object ends. Dispose then persists what either wrote. A host
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
        Run.EndGame();
    }

    // End, then the saves written at once, for a process the operating system ends as soon as the host
    // returns. The host's end and Dispose after it write nothing more.
    internal void EndAndPersist()
    {
        End();
        FlushSaves();
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
                DropPrefetch();
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
        _composer?.Entered(incoming.Scene.DocumentKey);
    }

    // Resolves, prepares and starts the incoming scene ahead of the outgoing one's teardown. A
    // failure here leaves the run on the scene it was on. A scene that fails to start was stopped by
    // its simulation.
    private SceneSimulation Bring(in SceneTransition target)
    {
        // The boundary consumes any prefetch. One that built this scene, however it named it, with an equal payload
        // is started with the collection it took. Any other is released unstarted before the scene is built and
        // collected with the payload it opens with.
        _prefetched = null;
        _reading = null;
        Scene next;
        AssetCollection preloads;
        if (_built is { } built && Opens(built.Target, target))
        {
            _built = null;
            (next, preloads) = (built.Scene, built.Preloads);
        }
        else
        {
            AbandonBuilt();
            next = _resolve(target);
            preloads = CollectPreloads(next, target.Payload);
        }

        Prepare(next, preloads, PrepareAssets);

        return new SceneSimulation(next, target.Payload, Run, _views);
    }

    // Collects a scene's preloads with the payload it opens with, under every host. A failure releases the
    // scene, which never started.
    private static AssetCollection CollectPreloads(Scene scene, IScenePayload? payload)
    {
        try
        {
            return scene.CollectAssetPreloads(payload);
        }
        catch (Exception failure)
        {
            Failure.CleanUp(failure, scene.Abandon, $"Collecting {scene.GetType().Name}'s assets and then releasing it both failed.");
            throw;
        }
    }

    // Hands a collected scene's preloads to prepare, which only a host with a device sets. A failure releases
    // the scene, which never started.
    private static void Prepare(Scene scene, AssetCollection preloads, Action<AssetCollection>? prepare)
    {
        try
        {
            prepare?.Invoke(preloads);
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

    private readonly record struct PrefetchedScene(SceneTransition Target, Scene Scene, AssetCollection Preloads);
}

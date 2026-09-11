using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Collision;
using Capsule.Rendering;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;
using Capsule.Scenes.Tiles;

namespace Capsule.Scenes;

/// <summary>
/// An ordered world of entities and a camera. Mutations requested during a step are deferred
/// until it ends; the first pending transition wins, except an exit request, which pre-empts one.
/// </summary>
public class Scene
{
    internal const string NoSourceYet =
        "the run's random source is not available yet; it is installed before the scene starts, so draw from OnStart on.";

    internal const string NoMixerYet =
        "the run's audio mixer is not available yet; it is installed before the scene starts, so play from OnStart on.";

    private readonly List<Entity> _entities = [];
    private readonly List<Entity> _pendingAdds = [];
    private readonly List<Entity> _pendingRemoves = [];

    // Membership of the two queues above. By reference, never by Equals: a game may give two
    // distinct entities an equality of their own and must still be able to hold both.
    private readonly HashSet<Entity> _pendingAddSet = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Entity> _pendingRemoveSet = new(ReferenceEqualityComparer.Instance);

    // Attached but not started. Starting is what lets an entity see its peers, so it waits until
    // everything arriving with it has attached rather than running per attach.
    private readonly List<Entity> _pendingStarts = [];

    private readonly List<Renderer> _renderers = [];
    private readonly List<Collider2D> _contactReporters = [];
    private readonly List<VisibleOnScreenNotifier2D> _screenNotifiers = [];

    // Traversal state for the two settle loops over the lists above, which handlers may add to and
    // remove from while the loop runs. Idle between settles, when neither list has a live cursor.
    private SettleCursor _contactSettle;
    private SettleCursor _notifierSettle;

    // The region the step's settle answered against, retained for the arrival settle that runs
    // after the deferred adds land: an OnStart there may install another camera, whose own region
    // is empty until its first late step, and the arrivals belong to the frame this step drew.
    private Rect _settledRegion;

    // Sort keys for the list above, retained across rebuilds so a banded scene does not allocate
    // one per rebuild. Only the first _renderers.Count entries mean anything.
    private long[] _rendererKeys = [];

    private Camera _camera = new();
    private RandomSource? _random;
    private AudioMixer? _audio;

    private bool _stepping;
    private bool _starting;
    private bool _started;

    private bool _stopped;
    private bool _renderersStale = true;
    private bool _drawing;
    private bool _rebuildDeferred;
    private bool _exitRequested;
    private string? _frameCapturePath;
    private SceneTransition? _transition;
    private TextureSampling? _sampling;

    /// <summary>An empty world, for a scene that builds itself in code.</summary>
    public Scene()
    {
    }

    /// <summary>
    /// The world a scene document describes: one <see cref="TileMap"/> or game entity per entry,
    /// in authored order. The document is construction data and is not retained.
    /// </summary>
    /// <exception cref="ArgumentNullException">The content carries no document or no entity registry.</exception>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public Scene(SceneContent content)
    {
        ArgumentNullException.ThrowIfNull(content.Document);
        ArgumentNullException.ThrowIfNull(content.Entities);

        foreach (SceneDocumentEntry entry in content.Document.Entries)
        {
            if (entry.TileMap is { } tileMap)
            {
                TileMap tiles = new(tileMap.Grid);
                if (tileMap.ZIndex is { } band)
                {
                    tiles.ZIndex = band;
                }

                Add(tiles);
                Size = Vector2.Max(Size, tiles.Size);
            }
            else if (entry.Entity is { } placed)
            {
                Entity spawned = content.Entities.Create(new EntitySpawn(
                    placed.Id,
                    placed.Type,
                    new Vector2(placed.X, placed.Y),
                    new Vector2(placed.ScaleX, placed.ScaleY)));

                // Only where the placement authors one, and after construction: the class owns the
                // default, and an authored band — 0 included — is what overrides it.
                if (placed.ZIndex is { } band)
                {
                    spawned.ZIndex = band;
                }

                Add(spawned);
            }
        }
    }

    /// <summary>
    /// The camera, always present; it opens spanning nothing unless the scene or the camera sets a
    /// span. Installing one cuts to it rather than sweeping from the previous centre.
    /// <para>
    /// Installed in a scene that has opened its camera, the incoming camera is notified at once —
    /// <see cref="Scenes.Camera.OnAddedToScene"/> then <see cref="Scenes.Camera.OnStart"/>, after
    /// the outgoing camera's <see cref="Scenes.Camera.OnRemovedFromScene"/>. Installed before that,
    /// it becomes the camera the scene opens with and is notified then; the camera it replaces is
    /// notified of nothing. A camera installed from within one of those hooks supersedes the
    /// handover that ran it, and a displaced camera is told only as much of its own handover as had
    /// already run.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException">The camera is null; a scene always has one.</exception>
    /// <exception cref="InvalidOperationException">The camera already frames another scene.</exception>
    public Camera Camera
    {
        get => _camera;
        protected set
        {
            ArgumentNullException.ThrowIfNull(value);
            RequireUnowned(value);

            Camera outgoing = _camera;
            _camera = value;

            // Installing the camera already installed is a cut and nothing else; running the
            // hooks would release a camera that never left.
            if (ReferenceEquals(outgoing.Scene, this) && !ReferenceEquals(outgoing, value))
            {
                // Cleared before the hook, so an outgoing camera reaching back cannot find the
                // scene still claiming it, and so a failed handover releases nothing twice.
                outgoing.Scene = null;
                outgoing.OnRemovedFromScene();

                // Whichever camera is current now: the hook may have installed another, and
                // installing the stale one would leave the scene naming a camera that has no handle.
                Install(_camera);
            }

            _camera.Retain();
        }
    }

    /// <summary>
    /// Everything in this scene that can be collided with. A <see cref="Collider2D"/> registers here
    /// when its entity joins the scene, and a <see cref="Tiles.TileMap"/> registers the grid it
    /// draws; game code queries it directly for rays, sweeps and overlaps.
    /// </summary>
    public CollisionWorld2D Collision { get; } = new();

    /// <summary>
    /// The run's deterministic random source: stream 0 of the seed the shell configured, the same
    /// instance for the whole run, so a scene transition neither reseeds nor rewinds it.
    /// Engine-owned. A domain whose draws must not move another's takes its own stream —
    /// <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scene has not started, so no source is installed yet.</exception>
    public RandomSource Random
    {
        get => _random ?? throw new InvalidOperationException(NoSourceYet);
        internal set => _random = value;
    }

    /// <summary>
    /// The run's audio mixer: the same instance for the whole run, so a voice a scene starts keeps
    /// playing across a transition unless whatever started it stops it. Engine-owned. A
    /// <see cref="Capsule.Scenes.Audio.AudioSource"/> on an entity is the per-entity way in; this is the way to
    /// play a sound no entity owns and to hold the game's bus volumes.
    /// <para>
    /// Installed after the scene is constructed and before its start, as <see cref="Random"/> is, so
    /// a constructor cannot level a bus. Bus volumes and bus pause state are the run's and persist
    /// across transitions: a game sets them once, from its boot scene's start or from a settings
    /// screen, and every scene after that plays into what was set.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The scene has not started, so no mixer is installed yet.</exception>
    public AudioMixer Audio
    {
        get => _audio ?? throw new InvalidOperationException(NoMixerYet);
        internal set => _audio = value;
    }

    // The mixer or nothing, for a component that must not throw where a scene has not started.
    internal AudioMixer? AudioOrNull => _audio;

    /// <summary>
    /// The screen layer's extent in canvas pixels, whose origin is its top-left corner and whose Y
    /// runs down. A run constant, engine-owned and installed before the scene starts: the run's
    /// declared render resolution, or the window size it was configured to open at, so it never
    /// follows a window the player resizes. A <see cref="ScreenEntity"/> is anchored and hit-tested
    /// against it, and a camera fit that
    /// reveals more world than the canvas holds leaves the screen layer this extent, centred in what
    /// the world was drawn on.
    /// </summary>
    public Vector2 Canvas { get; internal set; }

    /// <summary>
    /// World units the scene spans, from its origin at (0, 0); zero unless the scene sets it.
    /// A scene composed from a scene document with tile maps spans their largest dimensions.
    /// </summary>
    public Vector2 Size { get; protected set; }

    /// <summary>The colour behind everything the scene draws.</summary>
    public ColorRgba ClearColor { get; protected set; } = ColorRgba.Black;

    /// <summary>
    /// The sampling policy for world-space textures: the game's default, or
    /// <see cref="TextureSampling.Linear"/> where there is none, until the scene sets its own.
    /// </summary>
    public TextureSampling Sampling
    {
        get => _sampling ?? TextureSampling.Linear;
        protected set => _sampling = value;
    }

    /// <summary>State supplied by the transition that opened this scene.</summary>
    protected object? EntryPayload { get; private set; }

    /// <summary>Set by <see cref="RequestExit"/> and never cleared.</summary>
    public bool ExitRequested => _exitRequested;

    /// <summary>
    /// The path <see cref="CaptureFrame"/> asked the host to save the next drawn frame to, or
    /// null when no request is pending. Cleared when the host takes the request.
    /// </summary>
    public string? FrameCaptureRequested => _frameCapturePath;

    /// <summary>The entities held, in the order they were added. Invalidated by the next mutation.</summary>
    public ReadOnlySpan<Entity> Entities => CollectionsMarshal.AsSpan(_entities);

    /// <summary>
    /// Adds an unowned entity, deferred to the end of the current step when necessary.
    /// <para>
    /// A component may refuse the scene from its entry hook. The entity is then left in the scene
    /// with the components ahead of the refusal registered and the rest not; added during a step,
    /// the refusal surfaces where the queue is drained rather than from this call.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException">The entity is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The scene has stopped, the entity is already in a scene or queued, or, when the add is not
    /// deferred, a component refused the scene.
    /// </exception>
    public void Add(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ThrowIfStopped();

        if (entity.Scene is not null || _pendingAddSet.Contains(entity))
        {
            throw new InvalidOperationException(
                $"A {entity.GetType().Name} is already in a scene; an entity belongs to one at a time.");
        }

        if (_stepping)
        {
            _pendingAdds.Add(entity);
            _pendingAddSet.Add(entity);
            return;
        }

        Attach(entity);
    }

    /// <summary>
    /// Removes an entity, deferred and idempotent within the current step. One queued to join this
    /// step is accepted too: it attaches and detaches in the same drain, with symmetric hooks.
    /// </summary>
    /// <exception cref="ArgumentNullException">The entity is null.</exception>
    /// <exception cref="InvalidOperationException">The scene has stopped, or the entity is neither in it nor queued to join it.</exception>
    public void Remove(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ThrowIfStopped();

        if (!ReferenceEquals(entity.Scene, this) && !_pendingAddSet.Contains(entity))
        {
            throw new InvalidOperationException($"A {entity.GetType().Name} that this scene does not hold cannot be removed from it.");
        }

        if (_stepping)
        {
            if (_pendingRemoveSet.Add(entity))
            {
                _pendingRemoves.Add(entity);
            }

            return;
        }

        Detach(entity);
    }

    /// <summary>Finds the first active entity assignable to <typeparamref name="T"/>.</summary>
    public T? FindFirst<T>()
        where T : Entity
    {
        foreach (Entity entity in Entities)
        {
            if (entity is T found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Finds the only active entity assignable to <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">There is not exactly one matching entity.</exception>
    public T FindSingle<T>()
        where T : Entity
    {
        T? found = null;

        foreach (Entity entity in Entities)
        {
            if (entity is not T candidate)
            {
                continue;
            }

            if (found is not null)
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} holds more than one entity assignable to {typeof(T).Name}.");
            }

            found = candidate;
        }

        return found ?? throw new InvalidOperationException(
            $"A {GetType().Name} holds no entity assignable to {typeof(T).Name}.");
    }

    /// <summary>
    /// Asks the host to shut down once the current step finishes, replacing whatever transition
    /// was already pending.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scene has stopped.</exception>
    public void RequestExit()
    {
        ThrowIfStopped();

        _transition = SceneTransition.Exit();
        _exitRequested = true;
    }

    /// <summary>
    /// Asks the host to save the next frame it draws as a PNG at <paramref name="path"/>,
    /// overwriting whatever is there and creating the directory the path names.
    /// </summary>
    /// <param name="path">
    /// Where to write the PNG; a relative path resolves against the process working directory.
    /// </param>
    /// <remarks>
    /// <para>
    /// What is saved is the surface the world was drawn on: the declared render resolution where
    /// the run has one, and the back buffer where it has none, so the image does not follow the
    /// window's own size. Requesting again before the host takes the request replaces the path —
    /// the last request standing when a frame draws is the one served. A frame with no surface to
    /// draw on, as a minimised window has, leaves the request pending for the next frame that
    /// draws. A run with no graphics device at all — <c>RunHeadless</c>, or <c>--headless</c> —
    /// clears the request and writes nothing.
    /// </para>
    /// <para>
    /// The host creates the directories the path names. A save that fails — a device read-back or
    /// an encoding the backend refuses — writes no file, leaving whatever is at the path as it
    /// was, reports itself through <see cref="Capsule.Diagnostics.Log"/> at
    /// <see cref="Capsule.Diagnostics.LogLevel.Warning"/>, and drops the request rather than
    /// throwing into the frame loop or standing for another frame.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The path is null, empty or blank.</exception>
    /// <exception cref="InvalidOperationException">The scene has stopped.</exception>
    public void CaptureFrame(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfStopped();

        _frameCapturePath = path;
    }

    /// <summary>Asks the host to reconstruct this scene once the current step finishes.</summary>
    /// <exception cref="InvalidOperationException">The scene has stopped.</exception>
    public void RequestRestart() => TryRequest(SceneTransition.Restart(null, false));

    /// <summary>
    /// Asks the host to reconstruct this scene with <paramref name="payload"/> once the current
    /// step finishes.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scene has stopped.</exception>
    public void RequestRestart(object? payload) => TryRequest(SceneTransition.Restart(payload, true));

    /// <summary>Asks the host to replace this scene with <typeparamref name="TScene"/>.</summary>
    /// <exception cref="InvalidOperationException">The scene has stopped.</exception>
    public void RequestScene<TScene>(object? payload = null)
        where TScene : Scene =>
        TryRequest(SceneTransition.ToScene(typeof(TScene), payload));

    /// <summary>
    /// Asks the host to replace this scene with the scene the named document backs, or a plain
    /// <see cref="Scene"/> composed from it when no class claims it.
    /// </summary>
    /// <param name="name">The document's key under the scene root, without <c>.scene.json</c>.</param>
    /// <param name="payload">State offered to the next scene.</param>
    /// <exception cref="ArgumentException">The name is null or blank.</exception>
    /// <exception cref="InvalidOperationException">The scene has stopped.</exception>
    public void RequestScene(string name, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TryRequest(SceneTransition.ToName(name, payload));
    }

    /// <summary>
    /// Runs once, before the scene's first frame is built — where the camera opens. Exactly
    /// once: a scene belongs to one <see cref="SceneSimulation"/> for its lifetime.
    /// </summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once before the scene releases its entities, when it is replaced or its host ends.
    /// </summary>
    protected virtual void OnStop()
    {
    }

    /// <summary>Runs after positions are retained and before entities step.</summary>
    protected virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Runs after every entity's own <see cref="Entity.OnLateStep"/> and before the frame is built;
    /// use it for the scene's camera policy, which the camera's own
    /// <see cref="Scenes.Camera.OnLateStep"/> then frames.
    /// </summary>
    protected virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>
    /// Appends assets this scene declares beyond those owned by its entities and components.
    /// Collection may happen before <see cref="OnStart"/>, so declarations use construction-time
    /// state only. Override only to append declarations to <paramref name="assets"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is null.</exception>
    protected internal virtual void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
    }

    internal AssetCollection CollectAssetPreloads()
    {
        AssetCollection assets = new();
        CollectAssets(assets);

        foreach (Entity entity in _entities)
        {
            entity.CollectAssetPreloads(assets);
        }

        return assets;
    }

    internal void Start(object? entryPayload, in SceneDefaults defaults)
    {
        if (_started)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} has already been started; a scene belongs to one simulation.");
        }

        _started = true;
        EntryPayload = entryPayload;

        // Whatever the scene's own construction set stands; the game default fills in behind it.
        _sampling ??= defaults.Sampling;

        // Ahead of every start below, so an entity anchoring itself to a canvas edge finds one.
        Canvas = defaults.ResolvedCanvas;

        // Everything the scene was composed from is attached by now, so an entity starting here
        // can search the scene and find every other entry.
        StartPending();

        // Then whichever camera those starts left in place, so a camera that discovers its subject
        // finds an entity that has already started.
        Install(_camera);

        OnStart();

        // Wherever OnStart left the camera is where the scene opens: a scene's first frame never
        // interpolates.
        Camera.Retain();
    }

    internal void Stop()
    {
        if (!_started || _stopped)
        {
            return;
        }

        _stopped = true;
        List<Exception>? failures = null;

        try
        {
            OnStop();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        // The camera goes first, in reverse of the order Start installed it, so it is released
        // while the entities it framed are still here — and only if it was ever installed.
        if (ReferenceEquals(_camera.Scene, this))
        {
            try
            {
                Camera outgoing = _camera;
                outgoing.Scene = null;
                outgoing.OnRemovedFromScene();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        ReleaseEntities(ref failures);
        ClearPendingState();
        ThrowCleanupFailures(failures);
    }

    // Releases a composed scene the host rejected before start. Composition has already run the
    // structural entry hooks, so their removal counterparts still run; temporal hooks do not.
    internal void Abandon()
    {
        if (_started)
        {
            throw new InvalidOperationException($"A started {GetType().Name} must be stopped, not abandoned.");
        }

        if (_stopped)
        {
            return;
        }

        _stopped = true;
        List<Exception>? failures = null;
        ReleaseEntities(ref failures);
        ClearPendingState();
        ThrowCleanupFailures(failures);
    }

    // Takes the pending capture request, clearing it. Unlike a transition this is not bound to a
    // step: the host calls it from the frame that will serve it, so an unserved request stands
    // across steps until one does.
    internal bool TryTakeFrameCapture(out string path)
    {
        if (_frameCapturePath is not { } requested)
        {
            path = "";
            return false;
        }

        _frameCapturePath = null;
        path = requested;
        return true;
    }

    internal bool TryTakeTransition(out SceneTransition transition)
    {
        if (_transition is not { } requested)
        {
            transition = default;
            return false;
        }

        _transition = null;
        transition = requested;
        return true;
    }

    // Draw order is the entity's ZIndex plus the renderer's, then entity order, then component
    // attachment order. Sorted when the list or a key changes, never per step, and never while a
    // frame is being drawn — the traversal is handed this list and walks it to the end.
    internal ReadOnlySpan<Renderer> RenderersInDrawOrder()
    {
        if (_renderersStale)
        {
            RebuildRenderers();
        }

        return CollectionsMarshal.AsSpan(_renderers);
    }

    // Held back while the frame is being drawn, whether it is a key or the set of renderers that
    // changed: the traversal walks the list it was handed, so rebuilding under it would drop a
    // renderer it has not reached or repeat one it has. The rebuild lands at the end of the draw.
    internal void InvalidateRenderers()
    {
        if (_drawing)
        {
            _rebuildDeferred = true;
            return;
        }

        _renderersStale = true;
    }

    internal void BeginDraw() => _drawing = true;

    internal void EndDraw()
    {
        _drawing = false;

        // Marked, not rebuilt: the next read does it, so a frame nothing reads costs no sort.
        if (_rebuildDeferred)
        {
            _rebuildDeferred = false;
            _renderersStale = true;
        }
    }

    // Whether a renderer from the frozen draw list is still this scene's to draw. An earlier Draw
    // this frame may have detached it or taken its entity out of the scene, and the frozen list
    // still holds it.
    internal bool Draws(Renderer renderer) => renderer.Entity is { } entity && Keeps(entity);

    // Held and not on its way out. An entity queued for removal never steps, so it must never
    // start either — nor start the components it holds.
    internal bool Keeps(Entity entity) =>
        ReferenceEquals(entity.Scene, this) && !_pendingRemoveSet.Contains(entity);

    // The tick being stepped, and null outside a step. It is what lets an object act on the tick
    // it was told something in rather than on its own position in the step order.
    internal long? SteppingTick { get; private set; }

    internal void BeginStep()
    {
        _stepping = true;

        Camera.Retain();

        foreach (Entity entity in Entities)
        {
            entity.PreviousPosition = entity.Position;
        }
    }

    internal void RunStep(in StepContext context)
    {
        SteppingTick = context.Tick;
        OnStep(context);
    }

    internal void StepEntities(in StepContext context)
    {
        foreach (Entity entity in Entities)
        {
            entity.RunStep(context);
        }
    }

    internal void LateStepEntities(in StepContext context)
    {
        foreach (Entity entity in Entities)
        {
            entity.RunLateStep(context);
        }
    }

    internal void SettleContacts()
    {
        _contactSettle.Begin(_contactReporters.Count);
        try
        {
            while (_contactSettle.TryTake(_contactReporters.Count, out int index))
            {
                _contactReporters[index].SettleContacts();
            }
        }
        finally
        {
            _contactSettle.End();
        }
    }

    // By reference, never by Equals: two distinct colliders that compare equal must both report.
    internal void TrackContacts(Collider2D collider)
    {
        for (int index = 0; index < _contactReporters.Count; index++)
        {
            if (ReferenceEquals(_contactReporters[index], collider))
            {
                return;
            }
        }

        _contactReporters.Add(collider);
    }

    internal void UntrackContacts(Collider2D collider)
    {
        for (int index = 0; index < _contactReporters.Count; index++)
        {
            if (ReferenceEquals(_contactReporters[index], collider))
            {
                _contactReporters.RemoveAt(index);
                _contactSettle.Removed(index);
                return;
            }
        }
    }

    internal void RunLateStep(in StepContext context)
    {
        OnLateStep(context);
        Camera.OnLateStep(context);

        // The framing is final here, so what the frame will show is known before anything is told
        // about it: every notifier settles against the one region this step drew.
        Camera.SettleVisibleRegion();
        SettleScreenNotifiers();
    }

    // Bound by the same rule the contact reporters are: a handler may take notifiers out of the
    // scene, and every one still registered when the loop reaches it must still settle this step.
    // The cursor parks at the end rather than closing: whatever registers behind that mark while
    // the step's deferred adds land is the window SettleArrivedNotifiers owns.
    private void SettleScreenNotifiers()
    {
        _settledRegion = Camera.VisibleRegion;

        _notifierSettle.Begin(_screenNotifiers.Count);
        try
        {
            while (_notifierSettle.TryTake(_screenNotifiers.Count, out int index))
            {
                _screenNotifiers[index].SettleVisibility(_settledRegion);
            }
        }
        finally
        {
            _notifierSettle.Park(_screenNotifiers.Count);
        }
    }

    // An entity the step's deferred adds landed is drawn by the frame about to be rewritten, so its
    // notifier answers for that frame too, against the region that frame was drawn with rather than
    // whatever camera is installed by the time this runs. The window is the arrivals and only them:
    // one registered from inside a handler here lands past the end and first settles next step,
    // exactly as one registered during the ordinary settle.
    private void SettleArrivedNotifiers()
    {
        _notifierSettle.Resume(_screenNotifiers.Count);
        try
        {
            while (_notifierSettle.TryTake(_screenNotifiers.Count, out int index))
            {
                _screenNotifiers[index].SettleVisibility(_settledRegion);
            }
        }
        finally
        {
            _notifierSettle.End();
        }
    }

    // By reference, as the contact reporters are: two notifiers that compare equal must both settle.
    internal void TrackVisibility(VisibleOnScreenNotifier2D notifier)
    {
        for (int index = 0; index < _screenNotifiers.Count; index++)
        {
            if (ReferenceEquals(_screenNotifiers[index], notifier))
            {
                return;
            }
        }

        _screenNotifiers.Add(notifier);
    }

    internal void UntrackVisibility(VisibleOnScreenNotifier2D notifier)
    {
        for (int index = 0; index < _screenNotifiers.Count; index++)
        {
            if (ReferenceEquals(_screenNotifiers[index], notifier))
            {
                _screenNotifiers.RemoveAt(index);
                _notifierSettle.Removed(index);
                return;
            }
        }
    }

    internal void EndStep()
    {
        try
        {
            DrainPending();

            // Still inside the step, so what a handler here spawns queues and lands in the second
            // drain rather than attaching alone the way a between-steps add does.
            SettleArrivedNotifiers();
            DrainPending();
        }
        finally
        {
            // The step is over however the drain went; leaving this set would refuse every
            // mutation the game made afterwards. The tick goes with it: what happens between
            // steps belongs to no tick.
            _stepping = false;
            SteppingTick = null;
        }
    }

    // Deferral stays active while lifecycle hooks grow either queue. A cursor over a live Count
    // keeps the drain linear; dropping the processed prefix in a finally is what keeps an entity
    // that refused this scene from being tried again next step.
    private void DrainPending()
    {
        while (_pendingAdds.Count > 0 || _pendingRemoves.Count > 0 || _pendingStarts.Count > 0)
        {
            int processed = 0;
            try
            {
                while (processed < _pendingAdds.Count)
                {
                    Entity pending = _pendingAdds[processed];

                    // Counted as dealt with before the attempt, so one that throws goes too.
                    processed++;
                    Attach(pending);
                }
            }
            finally
            {
                Forget(_pendingAdds, _pendingAddSet, processed);
            }

            processed = 0;
            try
            {
                while (processed < _pendingRemoves.Count)
                {
                    Entity pending = _pendingRemoves[processed];
                    processed++;
                    Detach(pending);
                }
            }
            finally
            {
                Forget(_pendingRemoves, _pendingRemoveSet, processed);
            }

            // After both queues, so a batch spawned together starts once all of it has
            // attached; whatever an OnStart queues is drained by the next turn of this loop.
            StartPending();
        }
    }

    // Membership is reference identity: a subclass may override Equals, and two distinct instances
    // that compare equal must never stand in for each other here.
    internal static int IndexOf<T>(List<T> items, T item)
        where T : class
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    // Drops the processed prefix from a queue and from the set that mirrors it.
    private static void Forget(List<Entity> queue, HashSet<Entity> membership, int processed)
    {
        for (int index = 0; index < processed; index++)
        {
            membership.Remove(queue[index]);
        }

        queue.RemoveRange(0, processed);
    }

    private void Attach(Entity entity)
    {
        // Idempotent, because the drain hands an entity over before it knows the attach succeeds.
        if (entity.Scene is not null)
        {
            return;
        }

        _entities.Add(entity);
        _renderersStale = true;
        entity.Scene = this;

        // Components before the entity's own hook: one attached from inside OnAddedToScene is
        // notified by Entity.Add instead, so nothing is reached twice and nothing is missed.
        entity.EnterScene();
        entity.OnAddedToScene();

        _pendingStarts.Add(entity);

        // Attached outside a step and outside a drain, this entity arrived alone, so its moment
        // to start is now. During a step or a drain, EndStep starts the whole batch at once.
        if (_started && !_stepping)
        {
            StartPending();
        }
    }

    // Re-entrant by design: an OnStart may attach another entity, whose own Attach reaches here
    // and returns to let this loop take it — the queue is one drain, however deeply it is fed.
    private void StartPending()
    {
        if (_starting)
        {
            return;
        }

        _starting = true;

        try
        {
            int processed = 0;
            try
            {
                while (processed < _pendingStarts.Count)
                {
                    Entity pending = _pendingStarts[processed];
                    processed++;

                    // Attached and detached within the same drain, or queued for removal by a peer
                    // that started ahead of it: it never reaches a step, so time never begins for it.
                    if (Keeps(pending))
                    {
                        pending.RunStart();
                    }
                }
            }
            finally
            {
                _pendingStarts.RemoveRange(0, processed);
            }
        }
        finally
        {
            _starting = false;
        }
    }

    private void Install(Camera camera)
    {
        RequireUnowned(camera);

        // Set before the hook: a camera whose OnAddedToScene throws has entered the scene, and
        // Stop must still release it.
        camera.Scene = this;
        camera.OnAddedToScene();

        // The hook may have installed another camera, which released this one. Starting a camera
        // the scene has let go of would begin time for something already removed.
        if (!ReferenceEquals(_camera, camera))
        {
            return;
        }

        camera.RunStart();
    }

    private void RequireUnowned(Camera camera)
    {
        if (camera.Scene is not null && !ReferenceEquals(camera.Scene, this))
        {
            throw new InvalidOperationException(
                $"A {camera.GetType().Name} is already framing a scene; a camera belongs to one scene at a time.");
        }
    }

    private void Detach(Entity entity)
    {
        int held = IndexOf(_entities, entity);
        if (held >= 0)
        {
            DetachAt(held);
        }
    }

    private void DetachAt(int index)
    {
        Entity entity = _entities[index];
        _entities.RemoveAt(index);

        _renderersStale = true;
        entity.Scene = null;
        entity.OnRemovedFromScene();
        entity.LeaveScene();
    }

    private void ReleaseEntities(ref List<Exception>? failures)
    {
        for (int index = _entities.Count - 1; index >= 0; index--)
        {
            try
            {
                DetachAt(index);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
    }

    private void ClearPendingState()
    {
        _pendingStarts.Clear();
        _pendingAdds.Clear();
        _pendingAddSet.Clear();
        _pendingRemoves.Clear();
        _pendingRemoveSet.Clear();
        _renderers.Clear();
        _contactReporters.Clear();
        _renderersStale = false;
        _rebuildDeferred = false;
    }

    private static void ThrowCleanupFailures(List<Exception>? failures)
    {
        if (failures is [Exception failure])
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more scene cleanup hooks failed.", failures);
        }
    }

    private bool TryRequest(in SceneTransition transition)
    {
        ThrowIfStopped();

        if (_transition is not null)
        {
            return false;
        }

        _transition = transition;
        return true;
    }

    private void ThrowIfStopped()
    {
        if (_stopped)
        {
            throw new InvalidOperationException($"A stopped {GetType().Name} cannot be changed or request another transition.");
        }
    }

    private void RebuildRenderers()
    {
        _renderers.Clear();

        bool banded = false;
        foreach (Entity entity in Entities)
        {
            long band = entity.ZIndex;
            foreach (Component component in entity.Components)
            {
                if (component is Renderer renderer)
                {
                    banded |= band + renderer.ZIndex != 0;
                    _renderers.Add(renderer);
                }
            }
        }

        _renderersStale = false;

        // The walk yields entity order and then attachment order, which is exactly what an equal
        // key keeps, so a scene that bands nothing is already in draw order.
        if (banded)
        {
            SortRenderers();
        }
    }

    // Each key carries its renderer's walk position in its low bits, so no two keys are equal and
    // the runtime's unstable sort lands where a stable one would. The widened sum of two ints
    // spans exactly 33 signed bits, which leaves 31 for the position: a scene of 2^31 or more
    // renderers would collide two of them and lose the tie-break.
    private void SortRenderers()
    {
        int count = _renderers.Count;
        if (_rendererKeys.Length < count)
        {
            Array.Resize(ref _rendererKeys, Math.Max(count, _rendererKeys.Length * 2));
        }

        Span<Renderer> renderers = CollectionsMarshal.AsSpan(_renderers);
        Span<long> keys = _rendererKeys.AsSpan(0, count);
        for (int index = 0; index < count; index++)
        {
            Renderer renderer = renderers[index];
            keys[index] = (EffectiveKey(renderer) << 31) | (long)index;
        }

        keys.Sort(renderers);
    }

    // Widened before the addition: two ints at the far end of their range sum past what an int
    // holds, and a wrapped key would sort a foreground band under a background one.
    private static long EffectiveKey(Renderer renderer) =>
        (long)renderer.Entity!.ZIndex + renderer.ZIndex;

    // A cursor over a list a handler may reorder under it, which holds the invariant that the
    // entries still owed a turn are exactly the live list's [_next, _end) slice. A removal before
    // the cursor pulls both bounds back; one inside the slice only shortens it; one past the end
    // is an entry the settle never owned. Entries appended during a settle land at or beyond _end,
    // so each turn goes to an entry the list held when the settle began and still holds now,
    // exactly once, whatever the handlers do.
    private struct SettleCursor
    {
        private int _next;
        private int _end;

        internal void Begin(int count)
        {
            _next = 0;
            _end = count;
        }

        // The live count is re-read every turn because tearing the scene down clears the list
        // outright, which no Removed call reports.
        internal bool TryTake(int count, out int index)
        {
            index = _next;
            if (_next >= _end || _next >= count)
            {
                return false;
            }

            _next++;
            return true;
        }

        internal void End()
        {
            _next = 0;
            _end = 0;
        }

        // Holds the mark a later pass resumes from as an empty slice, so the removals that arrive
        // in between move it exactly as they would move a live cursor.
        internal void Park(int count)
        {
            _next = count;
            _end = count;
        }

        // Reopens the parked mark as the start of a slice running to the live count: the entries
        // that registered since the park, and only those.
        internal void Resume(int count) => _end = count;

        internal void Removed(int index)
        {
            if (index < _next)
            {
                _next--;
                _end--;
            }
            else if (index < _end)
            {
                _end--;
            }
        }
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tiles;

namespace Capsule.Scenes;

/// <summary>
/// An ordered world of entities with a <see cref="Camera"/> that frames it and a
/// <see cref="Collision"/> world it collides through. Mutations requested during a step are
/// deferred until the step ends. Populate the scene with <see cref="Add"/>, search it with
/// <see cref="FindSingle{T}"/>, and override <see cref="OnStart"/>, <see cref="OnStep"/>,
/// <see cref="OnLateStep"/> and <see cref="CollectAssets"/> to give it behaviour and assets.
/// </summary>
public class Scene
{
    internal const string NoRunYet =
        "it has no run yet. Reach the run from OnStart onward, not from a constructor.";

    private readonly List<Entity> _entities = [];
    private readonly List<Entity> _pendingAdds = [];
    private readonly List<Entity> _pendingRemoves = [];

    // Membership checked by reference identity, not by Equals.
    private readonly HashSet<Entity> _pendingAddSet = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Entity> _pendingRemoveSet = new(ReferenceEqualityComparer.Instance);

    // Attached but not started. Entities start as a batch, so each can search the scene as it starts.
    private readonly List<Entity> _pendingStarts = [];

    private readonly SceneRenderIndex _renderIndex = new();
    private readonly SettleList<Collider2D> _contactReporters = new();
    private readonly SettleList<VisibleOnScreenNotifier2D> _screenNotifiers = new();

    // The frame's visible region, held so notifiers settle against it.
    private Rect _settledRegion;

    private Camera _camera = new();
    private Run? _run;

    // The scroll origin authored in the document, written to each camera installed.
    private readonly Vector2? _scrollOrigin;

    private bool _stepping;
    private bool _starting;
    private bool _started;

    private bool _stopped;
    private bool _drawing;
    private TextureSampling? _sampling;

    // Handed out one at a time to each particle emitter added, so every emitter in a scene draws its
    // own randomness stream. A new scene instance starts at 0.
    private ulong _nextParticleStream;

    /// <summary>An empty world, for a scene that builds itself in code.</summary>
    public Scene()
    {
    }

    /// <summary>
    /// The world a scene document describes: one <see cref="TileMap"/> or game entity per entry,
    /// in authored order. The document is construction data and is not retained.
    /// </summary>
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

                if (tileMap.ScrollFactor is { } factor)
                {
                    tiles.ScrollFactor = factor;
                }

                Add(tiles);
                Size = Vector2.Max(Size, tiles.Size);
            }
            else if (entry.Entity is { } placed)
            {
                // The spawn applies band and factor ahead of the entity body.
                Add(content.Entities.Create(new EntitySpawn(
                    placed.Id,
                    placed.Type,
                    new Vector2(placed.X, placed.Y),
                    new Vector2(placed.ScaleX, placed.ScaleY),
                    placed.ZIndex,
                    placed.ScrollFactor)));
            }
        }

        _scrollOrigin = content.Document.ScrollOrigin;
    }

    /// <summary>
    /// The camera framing this scene. A scene always has one, and installing another cuts to it.
    /// When the scene comes from a document that authors a scroll origin, that origin is written to
    /// the camera. A camera installed before the scene starts becomes the opening camera. One
    /// installed later runs its <see cref="Scenes.Camera.OnStart"/> immediately.
    /// </summary>
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

            // Installing the current camera is a cut.
            if (ReferenceEquals(outgoing.SceneOrNull, this) && !ReferenceEquals(outgoing, value))
            {
                outgoing.SceneOrNull = null;
                Install(value);
            }

            _camera.SavePrevious();
        }
    }

    /// <summary>
    /// Everything in this scene that can be collided with. A <see cref="Collider2D"/> registers here
    /// when its entity joins the scene, and a <see cref="Tiles.TileMap"/> registers the grid it
    /// draws. Game code queries this world directly for rays, sweeps and overlaps.
    /// </summary>
    public CollisionWorld2D Collision { get; } = new();

    /// <summary>
    /// The run that owns this scene's lifetime state and requests. It is installed before the scene
    /// starts and is shared by every scene the run opens.
    /// </summary>
    /// <exception cref="InvalidOperationException">The run has not been installed yet.</exception>
    public Run Run
    {
        get => _run ?? throw new InvalidOperationException($"A {GetType().Name} has not started, so {NoRunYet}");
        internal set => _run = value;
    }

    // The run or null for components that start before the scene does.
    internal Run? RunOrNull => _run;

    // The ordinal the next particle emitter added to this scene draws its randomness stream from: a
    // static counter is not deterministic across runs, and a draw from Run.Random would shift gameplay
    // whenever an effect is added.
    internal ulong NextParticleStream() => _nextParticleStream++;

    /// <summary>
    /// World units the scene spans from its origin at (0, 0). Zero unless the scene sets it.
    /// A scene composed from a document with tile maps spans their largest dimensions.
    /// </summary>
    public Vector2 Size { get; protected set; }

    /// <summary>The colour behind everything the scene draws.</summary>
    public ColorRgba ClearColor { get; protected set; } = ColorRgba.Black;

    /// <summary>
    /// The sampling policy for world-space textures. Defaults to the game's setting, or to
    /// <see cref="TextureSampling.Linear"/> when the game has none, until the scene sets its own.
    /// </summary>
    public TextureSampling Sampling
    {
        get => _sampling ?? TextureSampling.Linear;
        protected set => _sampling = value;
    }

    /// <summary>State supplied by the transition that opened this scene.</summary>
    protected object? EntryPayload { get; private set; }

    /// <summary>
    /// Every entity held in step order: roots in addition order, each followed by its subtree.
    /// Invalidated by mutations.
    /// </summary>
    public ReadOnlySpan<Entity> Entities => CollectionsMarshal.AsSpan(_entities);

    /// <summary>
    /// Adds a root entity and its subtree. During a step the add is deferred to the end of the step.
    /// An entity with a parent joins through that parent and is refused here.
    /// <para>
    /// A component may refuse the scene from its entry hook. The entity stays in the scene with the
    /// components before it registered and the rest unregistered. If the add was deferred, the
    /// refusal surfaces at the queue drain instead of from this call.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The scene has stopped, the entity is already in a scene or queued, the entity has a parent,
    /// or a component refused the scene.
    /// </exception>
    public void Add(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.Parent is { } parent)
        {
            throw new InvalidOperationException(
                $"A {entity.GetType().Name} is parented under a {parent.GetType().Name}. Add the root of the tree instead.");
        }

        Enqueue(entity);
    }

    /// <summary>
    /// Removes an entity and its subtree. During a step the remove is deferred and idempotent.
    /// An entity still queued for addition attaches and detaches in the same drain, with matching
    /// hooks. Children detach first, deepest and last-parented first. A child removed by itself
    /// releases its <see cref="Entity.Parent"/> and becomes a root. Every removal hook runs, and
    /// failures propagate once detachment finishes.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scene has stopped, or the entity is not in it.</exception>
    public void Remove(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ThrowIfStopped();

        if (!ReferenceEquals(entity.SceneOrNull, this) && !_pendingAddSet.Contains(entity))
        {
            throw new InvalidOperationException($"This scene does not hold the {entity.GetType().Name} being removed.");
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
    /// Runs before the scene's first frame is built, which is when the camera opens. A scene belongs
    /// to a single <see cref="SceneSimulation"/> for its lifetime and never starts twice.
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

    /// <summary>Runs after the previous positions are saved and before entities step.</summary>
    protected virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Runs after every entity's <see cref="Entity.OnLateStep"/> and before the frame is built. Set
    /// the scene's camera policy here. The camera's own <see cref="Scenes.Camera.OnLateStep"/> runs
    /// afterwards and frames the result.
    /// </summary>
    protected virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>
    /// Draws the scene's own debug geometry, as <see cref="Component.OnDebugDraw"/> describes.
    /// Draws nothing by default. The camera, the entities and their components draw after it.
    /// </summary>
    protected virtual void OnDebugDraw()
    {
    }

    /// <summary>
    /// Fills the scene's section of the development overlay's scene page, as
    /// <see cref="Component.OnDebugPanel"/> describes. Writes nothing by default. The run's seed,
    /// <see cref="Size"/>, <see cref="ClearColor"/>, <see cref="Sampling"/> and the camera's centre
    /// are written before this call, and the scene's entities are listed after it.
    /// </summary>
    protected virtual void OnDebugPanel(DebugPanel panel)
    {
    }

    /// <summary>
    /// Appends assets this scene declares beyond those owned by its entities and components.
    /// Collection can run before <see cref="OnStart"/>, so declare from construction-time state.
    /// An override appends to <paramref name="assets"/> and changes nothing else.
    /// </summary>
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

    internal void Start(object? entryPayload)
    {
        if (_started)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} has already started. Build a new scene for a second simulation.");
        }

        _started = true;
        EntryPayload = entryPayload;

        // The game default fills in behind the scene's own setting.
        _sampling ??= Run.Sampling;

        // Attach everything composed at construction before any of it starts.
        StartPending();

        // Install the camera after starts so it finds entities that have started.
        Install(_camera);

        OnStart();

        // A scene's first frame never interpolates.
        Camera.SavePrevious();
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

        // Release the camera before the entities it framed.
        if (ReferenceEquals(_camera.SceneOrNull, this))
        {
            _camera.SceneOrNull = null;
        }

        ReleaseEntities(ref failures);
        ClearPendingState();
        ThrowCleanupFailures(failures);
    }

    // Release a composed scene that was rejected before it started. Structural removal hooks run,
    // temporal ones do not.
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

    // Throw a single failure or all aggregated. Cleanup runs to completion before throwing.
    internal static void ThrowCleanupFailures(List<Exception>? failures)
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

    internal ReadOnlySpan<Renderer> RenderersInDrawOrder() => _renderIndex.GetDrawOrder(Entities);

    internal void InvalidateRenderers() => _renderIndex.Invalidate(_drawing);

    internal void BeginDraw() => _drawing = true;

    internal void EndDraw()
    {
        _drawing = false;
        _renderIndex.EndDraw();
    }

    // Whether a renderer from the frozen draw list is still in this scene.
    internal bool Draws(Renderer renderer) => renderer.Entity is { } entity && Contains(entity);

    // Held and not queued for removal.
    internal bool Contains(Entity entity) =>
        ReferenceEquals(entity.SceneOrNull, this) &&
        (_pendingRemoveSet.Count == 0 || !_pendingRemoveSet.Contains(entity));

    // The current stepping tick, or null if not stepping.
    internal long? SteppingTick { get; private set; }

    internal void BeginStep()
    {
        _stepping = true;

        Camera.SavePrevious();

        foreach (Entity entity in Entities)
        {
            entity.SavePrevious();

            foreach (Component component in entity.Components)
            {
                component.SavePrevious();
            }
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
        _contactReporters.Begin();
        try
        {
            while (_contactReporters.TryNext(out Collider2D? collider))
            {
                collider.SettleContacts();
            }
        }
        finally
        {
            _contactReporters.End();
        }
    }

    internal void TrackContacts(Collider2D collider) => _contactReporters.Add(collider);

    internal void UntrackContacts(Collider2D collider) => _contactReporters.Remove(collider);

    internal void RunLateStep(in StepContext context)
    {
        OnLateStep(context);
        Camera.OnLateStep(context);

        // The frame's visible region is final. Notifiers settle against it after deferred adds land.
        Camera.SettleVisibleRegion();
        _settledRegion = Camera.VisibleRegion;
    }

    internal void TrackVisibility(VisibleOnScreenNotifier2D notifier) => _screenNotifiers.Add(notifier);

    internal void UntrackVisibility(VisibleOnScreenNotifier2D notifier) => _screenNotifiers.Remove(notifier);

    // Draw the scene, camera, and entities in step order after the step settles and deferred adds land.
    internal void RunDebugDraw()
    {
        OnDebugDraw();
        Camera.OnDebugDraw();

        foreach (Entity entity in Entities)
        {
            entity.RunDebugDraw();
        }
    }

    // Fill the scene page's section. The hook runs only if the scene has started.
    internal void RunDebugPanel(DebugPanel panel)
    {
        panel.Section("Scene");
        panel.Field("Seed", Run.Random.Seed.ToString(CultureInfo.InvariantCulture));
        panel.Field("Size", Size);
        panel.Field("ClearColor", ClearColor);
        panel.Field("Sampling", Sampling);
        panel.Field("Camera", Camera.Center);

        if (_started)
        {
            OnDebugPanel(panel);
        }
    }

    internal void EndStep()
    {
        try
        {
            DrainPending();
            SettleNotifiers();
        }
        finally
        {
            // The step is over however the drain went.
            _stepping = false;
            SteppingTick = null;
        }
    }

    // Drain queues while lifecycle hooks grow them. Dropped entities are not retried next step.
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

                    // Mark as processed before attempting attach so failures are not retried.
                    processed++;
                    Attach(pending);
                }
            }
            finally
            {
                DropProcessed(_pendingAdds, _pendingAddSet, processed);
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
                DropProcessed(_pendingRemoves, _pendingRemoveSet, processed);
            }

            // Start pending after draining both queues so the batch starts together.
            StartPending();
        }
    }

    // Settle registered notifiers against the frame region. One registered during the walk waits
    // for the next step.
    private void SettleNotifiers()
    {
        _screenNotifiers.Begin();
        try
        {
            while (_screenNotifiers.TryNext(out VisibleOnScreenNotifier2D? notifier))
            {
                notifier.SettleVisibility(_settledRegion);
            }
        }
        finally
        {
            _screenNotifiers.End();
        }
    }

    // Enqueue for addition. A child parented under a held entity joins directly.
    internal void Enqueue(Entity entity)
    {
        ThrowIfStopped();

        if (entity.SceneOrNull is not null || _pendingAddSet.Contains(entity))
        {
            throw new InvalidOperationException(
                $"A {entity.GetType().Name} is already in a scene. Remove it before adding it elsewhere.");
        }

        if (_stepping)
        {
            _pendingAdds.Add(entity);
            _pendingAddSet.Add(entity);
            entity.PendingScene = this;
            return;
        }

        Attach(entity);
    }

    // Drop processed items from the queue and its membership set.
    private static void DropProcessed(List<Entity> queue, HashSet<Entity> membership, int processed)
    {
        for (int index = 0; index < processed; index++)
        {
            membership.Remove(queue[index]);
            queue[index].PendingScene = null;
        }

        queue.RemoveRange(0, processed);
    }

    private void Attach(Entity entity)
    {
        // Idempotent: the drain may hand over an entity that already attached.
        if (entity.SceneOrNull is not null)
        {
            return;
        }

        AttachTree(entity);

        // Start immediately if attached outside a step. During a step, EndStep starts all together.
        if (_started && !_stepping)
        {
            StartPending();
        }
    }

    // Attach entity and children in tree order. Each lands after its parent so list stays in step order.
    private void AttachTree(Entity entity)
    {
        int index = entity.Parent is { } parent && ReferenceEquals(parent.SceneOrNull, this)
            ? IndexOf(parent) + HeldCount(parent)
            : _entities.Count;

        _entities.Insert(index, entity);
        _renderIndex.Invalidate(_drawing);
        entity.SceneOrNull = this;
        entity.PendingScene = null;

        // Enter components before the entity hook. Components attached from OnAddedToScene notify separately.
        entity.EnterScene();
        entity.OnAddedToScene();

        _pendingStarts.Add(entity);

        // A hook may parent another child here, so the list is re-read on every pass.
        for (int child = 0; child < entity.Children.Length; child++)
        {
            Entity next = entity.Children[child];
            if (next.SceneOrNull is null)
            {
                AttachTree(next);
            }
        }
    }

    // Count entities of a subtree held in this scene.
    private int HeldCount(Entity entity)
    {
        int count = 1;
        foreach (Entity child in entity.Children)
        {
            if (ReferenceEquals(child.SceneOrNull, this))
            {
                count += HeldCount(child);
            }
        }

        return count;
    }

    // Start pending entities. OnStart may attach more, and this loop drains those too.
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

                    // Skip entities that attached and detached or were removed before their turn.
                    if (Contains(pending))
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

        if (_scrollOrigin is { } origin)
        {
            camera.ScrollOrigin = origin;
        }

        camera.SceneOrNull = this;
        camera.RunStart();
    }

    private void RequireUnowned(Camera camera)
    {
        if (camera.SceneOrNull is not null && !ReferenceEquals(camera.SceneOrNull, this))
        {
            throw new InvalidOperationException(
                $"A {camera.GetType().Name} is already framing another scene. Install a different camera.");
        }
    }

    // Detach the entity and release its parent so it can be reparented or added as a root.
    private void Detach(Entity entity)
    {
        DetachTree(entity);
        entity.Unparent();
    }

    // Detach children before the entity, deepest and last-parented first. A hook may reparent, so
    // the list is re-read on every pass.
    private void DetachTree(Entity entity)
    {
        for (int child = entity.Children.Length - 1; child >= 0; child--)
        {
            if (child < entity.Children.Length)
            {
                DetachTree(entity.Children[child]);
            }
        }

        int held = IndexOf(entity);
        if (held >= 0)
        {
            DetachAt(held);
        }
    }

    private void DetachAt(int index)
    {
        Entity entity = _entities[index];
        _entities.RemoveAt(index);

        _renderIndex.Invalidate(_drawing);
        entity.SceneOrNull = null;
        entity.LeaveScene();
    }

    // Find by reference identity, not by Equals.
    private int IndexOf(Entity entity)
    {
        for (int index = 0; index < _entities.Count; index++)
        {
            if (ReferenceEquals(_entities[index], entity))
            {
                return index;
            }
        }

        return -1;
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
        _renderIndex.Clear();
        _contactReporters.Clear();
        _screenNotifiers.Clear();
    }

    private void ThrowIfStopped()
    {
        if (_stopped)
        {
            throw new InvalidOperationException($"A stopped {GetType().Name} cannot be changed or request another transition.");
        }
    }

    // A list that can be walked while its callbacks modify it. Entries added during a walk are
    // skipped until the next one.
    private sealed class SettleList<T>
        where T : class
    {
        private readonly List<T> _items = [];
        private int _next;
        private int _end;

        // Callers pair Add with Remove, so duplicates cannot build up.
        internal void Add(T item) => _items.Add(item);

        internal void Remove(T item)
        {
            for (int index = 0; index < _items.Count; index++)
            {
                if (!ReferenceEquals(_items[index], item))
                {
                    continue;
                }

                _items.RemoveAt(index);
                if (index < _next)
                {
                    _next--;
                    _end--;
                }
                else if (index < _end)
                {
                    _end--;
                }

                return;
            }
        }

        internal void Begin()
        {
            _next = 0;
            _end = _items.Count;
        }

        internal bool TryNext([NotNullWhen(true)] out T? item)
        {
            // The list may have been cleared by a callback.
            if (_next >= _end || _next >= _items.Count)
            {
                item = null;
                return false;
            }

            item = _items[_next++];
            return true;
        }

        internal void End()
        {
            _next = 0;
            _end = 0;
        }

        internal void Clear()
        {
            _items.Clear();
            End();
        }
    }
}

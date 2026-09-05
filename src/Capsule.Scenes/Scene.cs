using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Capsule.Assets;
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

    // What composing this scene's document asked for: its grids' textures, and the groups the
    // build derived for every spawn type it placed. The class's own groups join at DeclareTextures.
    private readonly List<TextureHandle> _composedTextures = [];

    private Camera _camera = new();
    private RandomSource? _random;
    private TextureSetBuilder? _declaredTextures;
    private IReadOnlyList<TextureHandle>? _textureSet;

    private bool _stepping;
    private bool _starting;
    private bool _started;

    private bool _stopped;
    private bool _renderersStale = true;
    private bool _exitRequested;
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
                Add(tiles);
                Size = Vector2.Max(Size, tiles.Size);

                if (tileMap.Grid.Texture is { } atlas)
                {
                    _composedTextures.Add(atlas);
                }
            }
            else if (entry.Entity is { } placed)
            {
                content.Entities.TexturesFor(placed.Type)?.Invoke(_composedTextures);

                Add(content.Entities.Create(new EntitySpawn(
                    placed.Id,
                    placed.Type,
                    new Vector2(placed.X, placed.Y),
                    new Vector2(placed.ScaleX, placed.ScaleY))));
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

    /// <summary>
    /// The textures the host keeps on the device while this scene runs, replacing what the build
    /// derived for it. Null, which is the default, takes that derivation: the textures its scene
    /// document names, plus the residency groups the code its spawn types and the class itself
    /// reach. A group is a generated directory's set — <c>GameAssets.Textures.Enemies.All</c>. An
    /// entity the scene's code can spawn is reached, whether the document names it or not; one
    /// chosen by data at run time is not, and its group goes here.
    /// <para>
    /// Read once, before the scene starts, so it cannot depend on state the scene builds in
    /// <see cref="OnStart"/>. Drawing a texture the set does not hold is a wiring fault the host
    /// raises by name.
    /// </para>
    /// </summary>
    protected internal virtual IReadOnlyList<TextureHandle>? ResidentTextures => null;

    // Everything this scene needs resident, settled on first read: the override where the scene
    // declares one, otherwise the derivation composed into it.
    internal IReadOnlyList<TextureHandle> TextureSet
    {
        get
        {
            if (_textureSet is not null)
            {
                return _textureSet;
            }

            if (ResidentTextures is { } declared)
            {
                return _textureSet = declared;
            }

            _declaredTextures?.Invoke(_composedTextures);
            _declaredTextures = null;

            return _textureSet = _composedTextures;
        }
    }

    // Hands the scene the groups its registration carries, before anything reads the set.
    internal void DeclareTextures(TextureSetBuilder? textures) => _declaredTextures = textures;

    /// <summary>Set by <see cref="RequestExit"/> and never cleared.</summary>
    public bool ExitRequested => _exitRequested;

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
    /// <param name="name">A scene document's bare name, as its authoring source is named.</param>
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
    /// Runs after entities step and before the frame is built; use it for the scene's camera
    /// policy, which the camera's own <see cref="Scenes.Camera.OnLateStep"/> then frames.
    /// </summary>
    protected virtual void OnLateStep(in StepContext context)
    {
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

        _pendingStarts.Clear();
        _pendingAdds.Clear();
        _pendingAddSet.Clear();
        _pendingRemoves.Clear();
        _pendingRemoveSet.Clear();
        _renderers.Clear();
        _contactReporters.Clear();
        _renderersStale = false;

        if (failures is [Exception failure])
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more scene cleanup hooks failed.", failures);
        }
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

    // Draw order is entity order, then component attachment order.
    internal ReadOnlySpan<Renderer> RenderersInDrawOrder()
    {
        if (_renderersStale)
        {
            RebuildRenderers();
        }

        return CollectionsMarshal.AsSpan(_renderers);
    }

    internal void InvalidateRenderers() => _renderersStale = true;

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

    internal void SettleContacts()
    {
        for (int index = 0; index < _contactReporters.Count;)
        {
            Collider2D reporter = _contactReporters[index];
            reporter.SettleContacts();

            // A handler may have stopped this reporter or one before it. Stay at this index when
            // the occupant changed, so the collider shifted into it still settles this step.
            if (index < _contactReporters.Count && ReferenceEquals(_contactReporters[index], reporter))
            {
                index++;
            }
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
                return;
            }
        }
    }

    internal void RunLateStep(in StepContext context)
    {
        OnLateStep(context);
        Camera.OnLateStep(context);
    }

    // Deferral stays active while lifecycle hooks grow either queue. A cursor over a live Count
    // keeps the drain linear; dropping the processed prefix in a finally is what keeps an entity
    // that refused this scene from being tried again next step.
    internal void EndStep()
    {
        try
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
        finally
        {
            // The step is over however the drain went; leaving this set would refuse every
            // mutation the game made afterwards. The tick goes with it: what happens between
            // steps belongs to no tick.
            _stepping = false;
            SteppingTick = null;
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

        foreach (Entity entity in Entities)
        {
            foreach (Component component in entity.Components)
            {
                if (component is Renderer renderer)
                {
                    _renderers.Add(renderer);
                }
            }
        }

        _renderersStale = false;
    }
}

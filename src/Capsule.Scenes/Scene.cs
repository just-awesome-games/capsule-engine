using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
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
    // One message for the scene, its entities and their components: the hazard is the same, and a
    // game reads whichever of the three it happened to ask.
    internal const string NoSourceYet =
        "the run's random source is not available yet; it is installed before the scene starts, so draw from OnStart on — an OnAddedToScene reached while the scene is still composing runs before it exists.";

    private readonly List<Entity> _entities = [];
    private readonly List<Entity> _pendingAdds = [];
    private readonly List<Entity> _pendingRemoves = [];

    // Membership of the two queues above, which both guard against queueing the same entity twice.
    // Scanning the queue for it made a step's worth of spawns cost the square of their number, and
    // a game that spawns a wave at once pays that where it can least afford to.
    //
    // By reference, never by Equals: a scene holds entities, not values, and a game that gives two
    // of them an equality of their own must still be able to have both. The rest of the scene
    // already answers that way.
    private readonly HashSet<Entity> _pendingAddSet = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Entity> _pendingRemoveSet = new(ReferenceEqualityComparer.Instance);

    // Attached but not started. Starting is what lets an entity see its peers, so it waits until
    // everything arriving with it has attached — the whole document at open, the whole drain
    // mid-step — rather than running per attach.
    private readonly List<Entity> _pendingStarts = [];

    private readonly List<Renderer> _renderers = [];
    private readonly List<Collider2D> _contactReporters = [];

    private Camera _camera = new();
    private RandomSource? _random;

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
            }
            else if (entry.Entity is { } placed)
            {
                Add(content.Entities.Create(new EntitySpawn(
                    placed.Id,
                    placed.Type,
                    new Vector2(placed.X, placed.Y),
                    new Vector2(placed.ScaleX, placed.ScaleY))));
            }
        }
    }

    /// <summary>
    /// The camera, always present. A scene installs its own — a <see cref="Scenes.Camera"/>
    /// subclass that frames itself in <see cref="Scenes.Camera.OnLateStep"/> — or moves the plain
    /// one it is given. It opens spanning nothing unless the scene or its camera sets a span.
    /// <para>
    /// Installing a camera cuts to it: the incoming camera opens where it is placed rather than
    /// sweeping in from wherever the previous one sat.
    /// </para>
    /// <para>
    /// Installed in a scene that has opened its camera, the incoming camera is notified at once —
    /// <see cref="Scenes.Camera.OnAddedToScene"/> then <see cref="Scenes.Camera.OnStart"/>, after
    /// the outgoing camera's <see cref="Scenes.Camera.OnRemovedFromScene"/>. Installed before that
    /// — from the scene's construction, or from an entity or component starting as the scene opens
    /// — it becomes the camera the scene opens with, and is notified then; the camera it replaces
    /// is notified of nothing, having never been the scene's.
    /// </para>
    /// <para>
    /// A camera installed from within one of those hooks supersedes the handover that ran it, and
    /// the scene ends up holding the innermost camera. A displaced camera is told only as much of
    /// its own handover as had already run: nothing at all if it was still pending, a paired
    /// arrival and departure if it had arrived.
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

                // Whichever camera is current now, not the one this call arrived with: the hook
                // may have installed another, and with no camera installed at that moment that
                // nested write only took the handle. Installing the stale one would leave the
                // scene naming one camera while another held the handle to it.
                Install(_camera);
            }

            // The incoming camera opens where it was placed: without this it would interpolate
            // from wherever the outgoing one had left PreviousCenter, and the swap would read as
            // a sweep across the world.
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
    /// Engine-owned, and installed before the scene starts — randomness is discovered in
    /// <see cref="OnStart"/>, never registered from a constructor or from an
    /// <c>OnAddedToScene</c> that runs while the scene is still composing.
    /// <para>
    /// This is the default stream. A game whose domains must not move one another gives each its
    /// own — <c>new RandomSource(Random.Seed, MyStreams.Map)</c> — so a map's draws cannot shift a
    /// shuffle's.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The scene has not started. The source is available from <see cref="OnStart"/> on, and in
    /// <c>OnAddedToScene</c> only for what is added after the scene has started.
    /// </exception>
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

    /// <summary>Set by <see cref="RequestExit"/> and never cleared.</summary>
    public bool ExitRequested => _exitRequested;

    /// <summary>The entities held, in the order they were added. Invalidated by the next mutation.</summary>
    public ReadOnlySpan<Entity> Entities => CollectionsMarshal.AsSpan(_entities);

    /// <summary>
    /// Adds an unowned entity, deferred to the end of the current step when necessary.
    /// <para>
    /// A component may refuse the scene from its entry hook — a collider needing a layer name the
    /// world has no room left to intern, or a <see cref="KinematicBody2D"/> whose collider is
    /// attached to some other entity. That is a programmer error, and the entity is left in the
    /// scene with the components ahead of the refusal registered and the rest not. Added during a
    /// step, the refusal surfaces where the queue is drained at the end of the step rather than
    /// from this call.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The entity is already in a scene or already queued; or, when the add is not deferred, a
    /// component refused the scene.
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
    /// <exception cref="InvalidOperationException">The entity is neither in this scene nor queued to join it.</exception>
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
    public void RequestExit()
    {
        ThrowIfStopped();

        _transition = SceneTransition.Exit();
        _exitRequested = true;
    }

    /// <summary>Asks the host to reconstruct this scene once the current step finishes.</summary>
    public void RequestRestart() => TryRequest(SceneTransition.Restart(null, false));

    /// <summary>
    /// Asks the host to reconstruct this scene with <paramref name="payload"/> once the current
    /// step finishes.
    /// </summary>
    public void RequestRestart(object? payload) => TryRequest(SceneTransition.Restart(payload, true));

    /// <summary>Asks the host to replace this scene with <typeparamref name="TScene"/>.</summary>
    public void RequestScene<TScene>(object? payload = null)
        where TScene : Scene =>
        TryRequest(SceneTransition.ToScene(typeof(TScene), payload));

    /// <summary>
    /// Asks the host to replace this scene with the scene the named document backs, or a plain
    /// <see cref="Scene"/> composed from it when no class claims it.
    /// </summary>
    /// <param name="name">A scene document's bare name, as its authoring source is named.</param>
    /// <param name="payload">State offered to the next scene.</param>
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
    /// policy — choosing a subject, ordering a cut — which the camera's own
    /// <see cref="Scenes.Camera.OnLateStep"/> then frames.
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

        // Then the camera it opens with — whichever camera those starts left in place, so one an
        // entity installed as it started is the one notified here, and a camera that discovers its
        // subject finds an entity that has already started.
        Install(_camera);

        OnStart();

        // Wherever OnStart left the camera is where the scene opens, not somewhere it slid in
        // from: a scene's first frame never interpolates, and neither does the one a transition
        // opens, since that scene is a new one starting here too.
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
        // while the entities it framed are still here — and only if it was ever installed, since a
        // scene whose entities failed to start never reached its camera.
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

    // Held and not on its way out. Starting is what begins time for an entity, and an entity queued
    // for removal never steps, so it must never start either — nor start the components it holds.
    internal bool Keeps(Entity entity) =>
        ReferenceEquals(entity.Scene, this) && !_pendingRemoveSet.Contains(entity);

    internal void BeginStep()
    {
        _stepping = true;

        Camera.Retain();

        foreach (Entity entity in Entities)
        {
            entity.PreviousPosition = entity.Position;
        }
    }

    internal void RunStep(in StepContext context) => OnStep(context);

    internal void StepEntities(in StepContext context)
    {
        foreach (Entity entity in Entities)
        {
            entity.RunStep(context);
        }
    }

    // Between the entity pass and the late step: every position this step is going to produce
    // has been produced, and a scene's own late-step policy already sees the settled contact set.
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

    // Scene policy before the camera settles: choosing a subject or ordering a cut is what the
    // camera then frames, so a scene that decides both in one step never lags a step behind.
    internal void RunLateStep(in StepContext context)
    {
        OnLateStep(context);
        Camera.OnLateStep(context);
    }

    // Keep deferral active while lifecycle hooks grow either queue.
    internal void EndStep()
    {
        // A cursor, and the processed prefix dropped in a finally. Indexing keeps the drain linear
        // in the queue — a hook may still grow it, and the loop re-reads Count — while dropping the
        // prefix however the drain ended is what keeps an entity that refuses this scene from being
        // tried again next step, and the ones attached before it from attaching twice.
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
            // mutation the game made afterwards.
            _stepping = false;
        }
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
        // Idempotent, because the drain hands an entity over before it knows the attach succeeds:
        // one already here has nothing left to do.
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
            // A cursor over a live Count, with the processed prefix dropped however the drain
            // ended, so an entity whose OnStart throws is not started again next drain.
            int processed = 0;
            try
            {
                while (processed < _pendingStarts.Count)
                {
                    Entity pending = _pendingStarts[processed];
                    processed++;

                    // Attached and detached within the same drain, or queued for removal by a peer
                    // that started ahead of it: it never reaches a step, so time never begins for
                    // it.
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

        // Set before the hook, not after: a camera whose OnAddedToScene throws has entered the
        // scene, and Stop must still release it.
        camera.Scene = this;
        camera.OnAddedToScene();

        // The hook may have installed another camera, which released this one and installed that
        // one in full. Starting a camera the scene has let go of would begin time for something
        // already removed.
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

    // Membership is reference identity: an entity subclass may override Equals, and two
    // distinct entities that compare equal must never stand in for each other here.
    private static int IndexOf(List<Entity> entities, Entity entity)
    {
        for (int index = 0; index < entities.Count; index++)
        {
            if (ReferenceEquals(entities[index], entity))
            {
                return index;
            }
        }

        return -1;
    }
}

using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Capsule.Collision;
using Capsule.Rendering;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Spawning;
using Capsule.Scenes.Tiles;

namespace Capsule.Scenes;

/// <summary>
/// An ordered world of entities and a camera. Mutations requested during a step are deferred
/// until it ends; the first pending transition wins.
/// </summary>
public class Scene
{
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

    // Scratch for the tag names one admission would have to intern. Reused rather than allocated
    // per attach, because spawning is common and the names are nearly always interned already.
    // Never nested: a component's OnAddingTo only asks questions, and cannot admit anything itself.
    private readonly List<string> _admissionTags = [];
    private readonly List<Renderer> _renderers = [];
    private readonly List<Collider2D> _contactReporters = [];

    private bool _stepping;
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
                    new Vector2(placed.X, placed.Y))));
            }
        }
    }

    /// <summary>
    /// The camera, always present. Game code moves it; it opens at the game's default span, or
    /// spanning nothing where there is none.
    /// </summary>
    public Camera Camera { get; } = new();

    /// <summary>
    /// Everything in this scene that can be collided with. A <see cref="Collider2D"/> registers here
    /// when its entity joins the scene, and a <see cref="Tiles.TileMap"/> registers the grid it
    /// draws; game code queries it directly for rays, sweeps and overlaps.
    /// </summary>
    public CollisionWorld2D Collision { get; } = new();

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
    /// Its components are asked first, and any of them may refuse — a collider needing more
    /// collision tag names than the world has room left to intern, one whose shape has no place
    /// where the entity stands, or a <see cref="KinematicMover2D"/> whose collider is attached to
    /// some other entity. A refusal leaves the entity out of the scene with none of its components
    /// registered. Added during a step, that refusal surfaces where the queue is drained at the end
    /// of the step rather than from this call.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The entity is already in a scene or already queued; or, when the add is not deferred, a
    /// component refused the scene.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// When the add is not deferred, a collider on the entity has no place at its position.
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

    /// <summary>Removes an entity, deferred and idempotent within the current step.</summary>
    /// <exception cref="InvalidOperationException">The entity is not in this scene.</exception>
    public void Remove(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ThrowIfStopped();

        if (!ReferenceEquals(entity.Scene, this))
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

    /// <summary>Asks the host to shut down once the current step finishes.</summary>
    public void RequestExit()
    {
        if (TryRequest(SceneTransition.Exit()))
        {
            _exitRequested = true;
        }
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

    /// <summary>Runs after positions are retained and before entities update.</summary>
    protected virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>Runs after entities update and before the frame is built; use it for camera policy.</summary>
    protected virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>Adds one entity per spawn, in source order.</summary>
    /// <exception cref="SpawnException">A spawn's type is claimed by no entity.</exception>
    protected void Spawn(ReadOnlySpan<EntitySpawn> spawns, EntityRegistry entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (EntitySpawn spawn in spawns)
        {
            Add(entities.Create(spawn));
        }
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

        // Whatever the scene's own construction set stands; the game default fills the rest, and
        // OnStart runs after both, so a camera opened there still wins.
        _sampling ??= defaults.Sampling;
        Camera.OpenAt(defaults.CameraViewport);

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

    internal void UpdateEntities(in StepContext context)
    {
        foreach (Entity entity in Entities)
        {
            entity.Update(context);
            entity.UpdateComponents(context);
        }
    }

    // Between the entity update and the late step: every position this step is going to produce
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

    internal List<string> BeginAdmission()
    {
        _admissionTags.Clear();

        return _admissionTags;
    }

    // Judged once over everything being admitted together — a collider asked on its own would
    // compare its one wanted name against a capacity its sibling is about to take — and then taken.
    //
    // Interning is the reservation. Checking capacity without claiming it leaves a gap: entry hooks
    // run in attachment order, and one ahead of a preflighted collider may legitimately intern a
    // tag of its own, or attach another collider, and spend the slot that collider was counting on.
    // Claiming here closes it, which is what lets the commit be a step that cannot fail: interning
    // is idempotent and permanent, so a collider that passed finds its name already in the table.
    // An admission refused above this line interns nothing.
    internal void ReserveTags(List<string> wanted)
    {
        int room = CollisionWorld2D.MaxTags - Collision.TagCount;

        if (wanted.Count > room)
        {
            throw new InvalidOperationException(
                $"Joining this scene would need {wanted.Count} more of the collision world's {CollisionWorld2D.MaxTags} tag names and only {room} are left; collision filtering is meant to name a handful of kinds, not every type in the game.");
        }

        for (int index = 0; index < wanted.Count; index++)
        {
            Collision.Tag(wanted[index]);
        }
    }

    internal void TrackContacts(Collider2D collider)
    {
        if (!_contactReporters.Contains(collider))
        {
            _contactReporters.Add(collider);
        }
    }

    internal void UntrackContacts(Collider2D collider) => _contactReporters.Remove(collider);

    internal void RunLateStep(in StepContext context) => OnLateStep(context);

    // Keep deferral active while lifecycle hooks grow either queue.
    internal void EndStep()
    {
        // A cursor, and the processed prefix dropped in a finally. Indexing keeps the drain linear
        // in the queue — a hook may still grow it, and the loop re-reads Count — while dropping the
        // prefix however the drain ended is what keeps an entity that refuses this scene from being
        // tried again next step, and the ones attached before it from attaching twice.
        try
        {
            while (_pendingAdds.Count > 0 || _pendingRemoves.Count > 0)
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

        // Asked before anything is published, so a component that cannot register with this scene
        // leaves the entity outside it rather than half in.
        entity.PreflightScene(this);

        _entities.Add(entity);
        _renderersStale = true;
        entity.Scene = this;

        // Components before the entity's own hook: one attached from inside OnAddedToScene is
        // notified by Entity.Add instead, so nothing is reached twice and nothing is missed.
        entity.EnterScene();
        entity.OnAddedToScene();
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

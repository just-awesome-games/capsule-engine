using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Capsule.Rendering;
using Capsule.Scenes.Spawning;

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
    private readonly List<Renderer> _renderers = [];

    private bool _stepping;
    private bool _started;
    private bool _stopped;
    private bool _renderersStale = true;
    private bool _exitRequested;
    private SceneTransition? _transition;
    private TextureSampling? _sampling;

    /// <summary>
    /// The camera, always present. Game code moves it; it opens at the game's default span, or
    /// spanning nothing where there is none.
    /// </summary>
    public Camera Camera { get; } = new();

    /// <summary>
    /// World units the scene spans, from its origin at (0, 0); zero unless the scene sets it.
    /// A scene built from a map takes it from its <see cref="Entities.TileMap"/>.
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

    /// <summary>Adds an unowned entity, deferred to the end of the current step when necessary.</summary>
    /// <exception cref="InvalidOperationException">The entity is already in a scene or already queued.</exception>
    public void Add(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ThrowIfStopped();

        if (entity.Scene is not null || IndexOf(_pendingAdds, entity) >= 0)
        {
            throw new InvalidOperationException(
                $"A {entity.GetType().Name} is already in a scene; an entity belongs to one at a time.");
        }

        if (_stepping)
        {
            _pendingAdds.Add(entity);
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
            if (IndexOf(_pendingRemoves, entity) < 0)
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

    /// <summary>Asks the host to replace this scene with the scene composed from a map.</summary>
    public void RequestScene(string mapName, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        TryRequest(SceneTransition.ToMap(mapName, payload));
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
        _pendingRemoves.Clear();
        _renderers.Clear();
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

    internal void RunLateStep(in StepContext context) => OnLateStep(context);

    // Keep deferral active while lifecycle hooks grow either queue.
    internal void EndStep()
    {
        // Indexed against a live Count, never a span: a hook may grow the list being drained.
        while (_pendingAdds.Count > 0 || _pendingRemoves.Count > 0)
        {
            for (int index = 0; index < _pendingAdds.Count; index++)
            {
                Attach(_pendingAdds[index]);
            }

            _pendingAdds.Clear();

            for (int index = 0; index < _pendingRemoves.Count; index++)
            {
                Detach(_pendingRemoves[index]);
            }

            _pendingRemoves.Clear();
        }

        _stepping = false;
    }

    private void Attach(Entity entity)
    {
        _entities.Add(entity);
        _renderersStale = true;
        entity.Scene = this;
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

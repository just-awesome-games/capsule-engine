using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>
/// One screen of game: every entity in play, and the camera looking at them. Subclass it and
/// compose the entities the screen is made of; a screen composed from a map subclasses
/// <see cref="MapScene"/>, which has already composed them.
/// Entities keep the order they were added in — never a hash order — and adding or removing one
/// during a step takes effect at the end of that step, so a step never iterates a list that
/// changes under it.
/// </summary>
public class Scene
{
    private readonly List<Entity> _entities = [];
    private readonly List<Entity> _pendingAdds = [];
    private readonly List<Entity> _pendingRemoves = [];
    private readonly List<Renderer> _renderers = [];

    private bool _stepping;
    private bool _started;
    private bool _renderersStale = true;

    /// <summary>The camera, always present. Game code moves it; it starts spanning nothing.</summary>
    public Camera Camera { get; } = new();

    /// <summary>
    /// World units the scene spans, from its origin at (0, 0); zero unless the scene sets it.
    /// A scene built from a map takes it from its <see cref="Entities.TileMap"/>.
    /// </summary>
    public Vector2 Size { get; protected set; }

    /// <summary>Set by <see cref="RequestExit"/> and never cleared.</summary>
    public bool ExitRequested { get; private set; }

    /// <summary>The entities held, in the order they were added. Invalidated by the next mutation.</summary>
    public ReadOnlySpan<Entity> Entities => CollectionsMarshal.AsSpan(_entities);

    /// <summary>
    /// Takes <paramref name="entity"/>, which no scene may already hold. Called during a step,
    /// the entity joins at the end of it, after every entity has updated.
    /// </summary>
    /// <exception cref="InvalidOperationException">The entity is already in a scene or already queued.</exception>
    public void Add(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

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

    /// <summary>
    /// Lets go of <paramref name="entity"/>. Called during a step, it stays for the rest of that
    /// step — it has already updated, or is about to — and leaves at the end of it. Idempotent
    /// within a step: two causes may remove the same entity in one step, and it detaches once.
    /// </summary>
    /// <exception cref="InvalidOperationException">The entity is not in this scene.</exception>
    public void Remove(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

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

    /// <summary>Asks the host to shut down; it does so once the current step finishes.</summary>
    public void RequestExit() => ExitRequested = true;

    /// <summary>
    /// Runs once, before the scene's first frame is built — where the camera opens. Exactly
    /// once: a scene belongs to one <see cref="SceneSimulation"/> for its lifetime.
    /// </summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once per fixed step, after every position is retained and before any entity updates:
    /// scene-wide input, and camera policy.
    /// </summary>
    protected virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>Adds one entity per spawn, in the order given.</summary>
    /// <exception cref="SpawnException">A spawn's type is claimed by no entity.</exception>
    protected void Spawn(ReadOnlySpan<EntitySpawn> spawns, EntityRegistry entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (EntitySpawn spawn in spawns)
        {
            Add(entities.Create(spawn));
        }
    }

    internal void Start()
    {
        if (_started)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} has already been started; a scene belongs to one simulation.");
        }

        _started = true;
        OnStart();
    }

    /// <summary>
    /// The renderers to draw, in entity order then attachment order within an entity: the draw
    /// order itself. Derived — marked stale by any structural change and rebuilt here, whole
    /// rather than appended to, because a renderer attached to an early entity draws in that
    /// entity's place. Maintaining it surgically is the upgrade path if mutation-heavy frames
    /// ever profile hot.
    /// </summary>
    internal ReadOnlySpan<Renderer> RenderersInDrawOrder()
    {
        if (_renderersStale)
        {
            RebuildRenderers();
        }

        return CollectionsMarshal.AsSpan(_renderers);
    }

    internal void InvalidateRenderers() => _renderersStale = true;

    /// <summary>Opens a step: retains every position, and defers scene changes to <see cref="EndStep"/>.</summary>
    internal void BeginStep()
    {
        _stepping = true;

        foreach (Entity entity in Entities)
        {
            entity.PreviousPosition = entity.Position;
        }
    }

    internal void RunStep(in StepContext context) => OnStep(context);

    /// <summary>Updates every entity in order, each followed by its own components.</summary>
    internal void UpdateEntities(in StepContext context)
    {
        foreach (Entity entity in Entities)
        {
            entity.Update(context);
            entity.UpdateComponents(context);
        }
    }

    /// <summary>
    /// Closes a step: everything queued during it lands, additions before removals, in the order
    /// it was queued. Deferral stays on for the whole drain, so a lifecycle hook that adds or
    /// removes joins these queues under the same coalescing rules rather than reaching the
    /// entity list directly, and what a hook queues lands before this returns.
    /// </summary>
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
            _entities.RemoveAt(held);
        }

        _renderersStale = true;
        entity.Scene = null;
        entity.OnRemovedFromScene();
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

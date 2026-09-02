using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Capsule.Scenes;

/// <summary>
/// One thing in a scene. World units, Y-down; what <see cref="Position"/> anchors — a corner, a
/// centre, a pair of feet — is the subclass's own convention. Subclass it for behaviour and
/// attach <see cref="Component"/>s for what composes.
/// </summary>
public class Entity
{
    private readonly List<Component> _components = [];

    private int _movementTrackers;
    private bool _started;

    /// <param name="position">
    /// Where the entity starts. <see cref="PreviousPosition"/> starts equal to it, so a spawn
    /// does not slide in from wherever the renderer would otherwise interpolate from.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    protected Entity(Vector2 position)
    {
        Position = position;
        PreviousPosition = position;
    }

    /// <summary>
    /// Where the entity is now, in world units. Always finite: a NaN or infinite position is
    /// refused rather than stored, because everything downstream reads it — the renderer
    /// interpolates it against <see cref="PreviousPosition"/>, and a collider on this entity hands
    /// it straight to the collision broadphase.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    /// <exception cref="InvalidOperationException">The entity is anchored to the world origin.</exception>
    public Vector2 Position
    {
        get;

        set
        {
            if (Anchored)
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} is anchored at the world origin and cannot be moved.");
            }

            RequireFinite(value);

            field = value;

            // The counter keeps the announcement free for an entity carrying no component that
            // tracks its position.
            if (_movementTrackers > 0)
            {
                NotifyMoved();
            }
        }
    }

    /// <summary>
    /// <see cref="Position"/> as of the previous step. Engine-managed: the scene retains it at
    /// the top of every step, and the renderer interpolates the pair by the frame alpha.
    /// </summary>
    public Vector2 PreviousPosition { get; internal set; }

    /// <summary>The scene holding this entity; null before it is added and after it is removed.</summary>
    public Scene? Scene { get; internal set; }

    // Set by a subclass whose contents are world coordinates, so a position write is a mistake
    // rather than a move.
    internal bool Anchored { get; init; }

    internal ReadOnlySpan<Component> Components => CollectionsMarshal.AsSpan(_components);

    // Every walk of the component list goes through this. A hook may detach the component being
    // visited or one before it, which shifts the rest left; the cursor holds its index when the
    // occupant changed, so the component shifted into it is visited rather than skipped. A collider
    // once entered a scene unregistered because one such walk advanced unconditionally.
    private ComponentWalk LiveComponents => new(_components);

    /// <summary>Moves immediately, with no interpolation from the old position.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    /// <exception cref="InvalidOperationException">The entity is anchored to the world origin.</exception>
    public void Teleport(Vector2 position)
    {
        // Position validates first, so a rejected teleport leaves both values as they were rather
        // than collapsing the interpolation pair onto a position the entity never reached.
        Position = position;
        PreviousPosition = position;
    }

    /// <summary>Attaches <paramref name="component"/>, which no entity may already own.</summary>
    /// <exception cref="InvalidOperationException">
    /// The component is already attached to an entity, or it refuses this entity — a
    /// <see cref="Physics.KinematicBody2D"/> offered to one that already holds a body.
    /// </exception>
    public void Add(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.Entity is not null)
        {
            throw new InvalidOperationException(
                $"A {component.GetType().Name} is already attached to a {component.Entity.GetType().Name}; a component belongs to one entity at a time.");
        }

        component.Entity = this;
        _components.Add(component);
        component.OnAttachedTo(this);

        // Attaching to an entity a scene already holds changes that scene's renderer set.
        Scene?.InvalidateRenderers();

        if (Scene is not null)
        {
            component.EnterScene();
        }

        // Time has already begun for this entity, so it has begun for whatever it takes on: the
        // component would otherwise step without ever having started. Both conditions are re-read
        // after the hooks above, either of which may have detached it or taken this entity out of
        // the scene. Kept, not merely held: an entity queued for removal mid-step still names its
        // scene but never steps again, so a component taken on now must wait for the next add,
        // which RunStart hands it.
        if (_started && Scene?.Keeps(this) == true && ReferenceEquals(component.Entity, this))
        {
            component.RunStart();
        }
    }

    /// <summary>Detaches <paramref name="component"/> so it may be attached elsewhere.</summary>
    /// <exception cref="InvalidOperationException">The component is not attached to this entity.</exception>
    public void Remove(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (!ReferenceEquals(component.Entity, this))
        {
            throw new InvalidOperationException(
                $"A {component.GetType().Name} that this entity does not hold cannot be removed from it.");
        }

        int held = IndexOf(component);
        _components.RemoveAt(held);

        // Cleared before the hooks, so a hook that reaches back through Entity cannot find this
        // entity still claiming a component it no longer holds.
        component.Entity = null;
        component.LeaveScene();
        component.OnDetachingFrom(this);
        Scene?.InvalidateRenderers();
    }

    /// <summary>Finds the first attached component assignable to <typeparamref name="T"/>.</summary>
    public bool TryGet<T>([NotNullWhen(true)] out T? component)
        where T : Component
    {
        foreach (Component candidate in Components)
        {
            if (candidate is T found)
            {
                component = found;
                return true;
            }
        }

        component = null;
        return false;
    }

    /// <summary>Gets the first attached component assignable to <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">No attached component is assignable to that type.</exception>
    public T Get<T>()
        where T : Component =>
        TryGet<T>(out T? component)
            ? component
            : throw new InvalidOperationException(
                $"A {GetType().Name} has no component assignable to {typeof(T).Name}.");

    /// <summary>
    /// Advances this entity by one fixed step, before its components step. Never reached before
    /// <see cref="OnStart"/>: an entity the scene holds but has not started takes no step, and
    /// neither do the components it holds.
    /// </summary>
    protected internal virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Runs once for this entity's lifetime — not again when it is added to a scene a second time —
    /// before its first step and after everything added alongside it, so the scene may be searched
    /// from here: an entity a scene document composed sees every other entry, and one of a batch
    /// spawned during a step sees the whole batch. Runs before the components held at that moment
    /// start; an entity that leaves the scene from here never steps, and so starts none of them,
    /// which leaves them to start if it is added again.
    /// </summary>
    protected internal virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once the scene holds this entity, with <see cref="Scene"/> set. Peers added alongside
    /// it may not exist yet: register with the scene here and discover it in
    /// <see cref="OnStart"/>.
    /// </summary>
    protected internal virtual void OnAddedToScene()
    {
    }

    /// <summary>
    /// Runs once the scene has let go of this entity, with <see cref="Scene"/> cleared — when the
    /// entity is removed, and when the scene stops. Anything <see cref="OnAddedToScene"/>
    /// registered is released here.
    /// </summary>
    protected internal virtual void OnRemovedFromScene()
    {
    }

    /// <summary>Counts the components that want telling when this entity moves.</summary>
    internal void TrackMovement(int delta) => _movementTrackers += delta;

    // The entity's own start is once for its lifetime; the component sweep is not. An entity
    // removed and added again reaches this a second time holding components that never started —
    // because it left before they could — and it is about to step them.
    internal void RunStart()
    {
        if (!_started)
        {
            _started = true;
            OnStart();
        }

        // OnStart may have taken this entity out of the scene, or queued it to leave at the end of
        // the drain. Either way it never steps, so its components must not start: they have been
        // told they left, and a component's OnStart is promised a scene to search.
        if (Scene?.Keeps(this) != true)
        {
            return;
        }

        // Each component's own flag makes a second call a no-op, so one attached from inside
        // OnStart is started once whichever path reaches it first.
        foreach (Component component in LiveComponents)
        {
            component.RunStart();
        }
    }

    internal void EnterScene()
    {
        // EnterScene is idempotent, so a component attached from another's hook and reached twice
        // registers once.
        foreach (Component component in LiveComponents)
        {
            component.EnterScene();
        }
    }

    internal void LeaveScene()
    {
        foreach (Component component in LiveComponents)
        {
            component.LeaveScene();
        }
    }

    // Nothing steps before it has started. An entity the scene still holds but never started —
    // one whose batch start was cut short — has no time begun for it, so neither it nor anything
    // it holds may be advanced.
    internal void RunStep(in StepContext context)
    {
        if (!_started)
        {
            return;
        }

        OnStep(context);
        StepComponents(context);
    }

    private void StepComponents(in StepContext context)
    {
        foreach (Component component in LiveComponents)
        {
            component.RunStep(context);
        }
    }

    private static void RequireFinite(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "An entity's position must be finite; a NaN or infinite one spreads to everything that reads it, from render interpolation to the collision broadphase.");
        }
    }

    private void NotifyMoved()
    {
        foreach (Component component in LiveComponents)
        {
            component.OnEntityMoved();
        }
    }

    private int IndexOf(Component component)
    {
        for (int index = 0; index < _components.Count; index++)
        {
            if (ReferenceEquals(_components[index], component))
            {
                return index;
            }
        }

        return -1;
    }

    private struct ComponentWalk(List<Component> components)
    {
        private Component? _visited;
        private int _index;

        // Public because the foreach pattern only binds to public members, on a type nothing
        // outside this class can name.
        public readonly Component Current => components[_index];

        public readonly ComponentWalk GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_visited is not null && _index < components.Count &&
                ReferenceEquals(components[_index], _visited))
            {
                _index++;
            }

            if (_index >= components.Count)
            {
                return false;
            }

            _visited = components[_index];
            return true;
        }
    }
}

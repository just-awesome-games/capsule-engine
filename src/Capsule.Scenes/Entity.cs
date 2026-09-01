using System.Collections.Concurrent;
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
    // The type that limits each component type to one per entity, resolved once and remembered:
    // the walk up a hierarchy looking for [DisallowMultipleComponent] is reflection, and Add runs
    // it on every attach. Concurrent because engine test classes run in parallel, and a null value
    // is a real answer — that component type carries no limit.
    private static readonly ConcurrentDictionary<Type, Type?> SingleInstanceTypes = new();

    private readonly List<Component> _components = [];

    private int _movementTrackers;

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
    /// <exception cref="ArgumentOutOfRangeException">
    /// The position is not finite, or a collider attached to this entity cannot step to it from
    /// where it stands — two positions at opposite ends of the float range have an infinite one
    /// between them.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A collider attached to this entity has no place at that position: its shape there covers a
    /// region no float box holds.
    /// </exception>
    public Vector2 Position
    {
        get;

        // Asked, then written, then announced. Every component that tracks position gets to refuse
        // the new one first — a collider whose shape could not be placed there, or whose step to it
        // overflows, throws from the preflight while this entity and all its siblings are still
        // untouched. Only once every one of them has accepted is the field written, which is what
        // makes the announcement below unable to fail: it carries no input the preflight has not
        // already validated. The counter keeps both passes free for an entity carrying no such
        // component.
        set
        {
            RequireFinite(value);

            if (_movementTrackers > 0)
            {
                NotifyMoving(value);
            }

            field = value;

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

    internal ReadOnlySpan<Component> Components => CollectionsMarshal.AsSpan(_components);

    /// <summary>Moves immediately, with no interpolation from the old position.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The position is not finite, or a collider attached to this entity cannot step to it from
    /// where it stands.
    /// </exception>
    /// <exception cref="ArgumentException">A collider attached to this entity has no place at that position.</exception>
    public void Teleport(Vector2 position)
    {
        // Position validates first, so a rejected teleport leaves both values as they were rather
        // than collapsing the interpolation pair onto a position the entity never reached.
        Position = position;
        PreviousPosition = position;
    }

    // Refused before OnAttachingTo, so nothing about either the entity or the offered component has
    // been touched. The limit belongs to the outermost type that declares it, not to the
    // component's own class: every subclass of a limited type shares its one slot, whichever of
    // them arrived first.
    private void RequireSingleInstanceIsFree(Component component)
    {
        if (SingleInstanceTypeOf(component.GetType()) is not { } single)
        {
            return;
        }

        for (int index = 0; index < _components.Count; index++)
        {
            Component held = _components[index];
            if (single.IsInstanceOfType(held))
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} already holds a {held.GetType().Name}, and {single.Name} permits one per entity; a {component.GetType().Name} cannot join it.");
            }
        }
    }

    // The outermost type from the component's own class up to Component that carries the attribute,
    // or null where none does. Outermost, not nearest: a marked subclass of a marked base would
    // otherwise be counted against a different slot from its base, and whether the two could share
    // an entity would turn on which of them was attached first. Declared attributes only, so the
    // walk finds the declarations themselves rather than the same one inherited at every level.
    private static Type? SingleInstanceTypeOf(Type component) =>
        SingleInstanceTypes.GetOrAdd(
            component,
            static type =>
            {
                Type? outermost = null;
                for (Type? current = type; current is not null && current != typeof(Component); current = current.BaseType)
                {
                    if (current.IsDefined(typeof(DisallowMultipleComponentAttribute), inherit: false))
                    {
                        outermost = current;
                    }
                }

                return outermost;
            });

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

    /// <summary>Attaches <paramref name="component"/>, which no entity may already own.</summary>
    /// <exception cref="ArgumentException">
    /// The component cannot live on this entity where it stands — a collider whose shape covers no
    /// region a float box holds at this entity's position.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The component is already attached to an entity; this entity already holds one of a type
    /// marked <see cref="DisallowMultipleComponentAttribute"/> that the component shares; or this
    /// entity is already in a scene and the component cannot register with it — a collider needing
    /// more collision layer names than the scene's world has room left to intern.
    /// </exception>
    public void Add(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.Entity is not null)
        {
            throw new InvalidOperationException(
                $"A {component.GetType().Name} is already attached to a {component.Entity.GetType().Name}; a component belongs to one entity at a time.");
        }

        RequireSingleInstanceIsFree(component);

        // Asked before it is held: a component that cannot live on this entity where it stands, or
        // cannot register with the scene that entity is already in, says so while neither of them
        // has changed. The siblings already here have interned whatever names they needed, so only
        // this one's are still to find room for.
        component.OnAttachingTo(this);

        if (Scene is { } joining)
        {
            List<string> layers = joining.BeginAdmission();
            component.OnAddingTo(joining, this, layers);
            joining.ReserveLayers(layers);
        }

        component.Entity = this;
        _components.Add(component);

        // Registered only now, with nothing left that can refuse: an increment taken before a
        // preflight that then threw would have nobody to take it back out.
        component.OnAttachedTo(this);

        // Attaching to an entity a scene already holds changes that scene's renderer set.
        Scene?.InvalidateRenderers();

        if (Scene is not null)
        {
            component.EnterScene();
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
        component.LeaveScene();
        component.OnDetachingFrom(this);
        component.Entity = null;
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

    /// <summary>Advances this entity by one fixed step, before its components update.</summary>
    public virtual void Update(in StepContext context)
    {
    }

    /// <summary>Runs once the scene holds this entity, with <see cref="Scene"/> set.</summary>
    protected internal virtual void OnAddedToScene()
    {
    }

    /// <summary>Runs once the scene has let go of this entity, with <see cref="Scene"/> cleared.</summary>
    protected internal virtual void OnRemovedFromScene()
    {
    }

    /// <summary>Counts the components that want telling when this entity moves.</summary>
    internal void TrackMovement(int delta) => _movementTrackers += delta;

    // Every component asked before any of them registers, so a scene one of them cannot join
    // leaves the entity outside it with no sibling half-registered. The layer names they want are
    // pooled into one list and judged against the world's remaining capacity once: asked one at a
    // time, two siblings each needing the last free slot would both be told yes.
    internal void PreflightScene(Scene scene)
    {
        List<string> layers = scene.BeginAdmission();

        for (int index = 0; index < _components.Count; index++)
        {
            _components[index].OnAddingTo(scene, this, layers);
        }

        // Taken, not merely counted, and taken before a single entry hook runs. Past this line the
        // registrations these components are about to make cannot fail for want of a layer: what
        // they were promised is already in the table, and a hook that interns one of its own — or
        // attaches another collider, which goes through Add and preflights against what is left —
        // can only spend what nobody here was counting on.
        scene.ReserveLayers(layers);
    }

    internal void EnterScene()
    {
        // Indexed against a live Count: a component may attach another from its own hook, and
        // EnterScene is idempotent, so reaching it twice is a no-op rather than a double
        // registration.
        for (int index = 0; index < _components.Count; index++)
        {
            _components[index].EnterScene();
        }
    }

    internal void LeaveScene()
    {
        for (int index = _components.Count - 1; index >= 0; index--)
        {
            if (index < _components.Count)
            {
                _components[index].LeaveScene();
            }
        }
    }

    internal void UpdateComponents(in StepContext context)
    {
        for (int index = 0; index < _components.Count;)
        {
            Component component = _components[index];
            component.Update(context);

            // A component may remove itself or one before it. Stay at this index when the
            // occupant changed so the component shifted into it still receives this step.
            if (index < _components.Count && ReferenceEquals(_components[index], component))
            {
                index++;
            }
        }
    }

    private void NotifyMoving(Vector2 position)
    {
        for (int index = 0; index < _components.Count; index++)
        {
            _components[index].OnEntityMoving(position);
        }
    }

    private void NotifyMoved()
    {
        for (int index = 0; index < _components.Count; index++)
        {
            _components[index].OnEntityMoved();
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
}

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
        for (int index = 0; index < _components.Count;)
        {
            Component component = _components[index];
            component.LeaveScene();

            // A hook may have detached this component or one before it. Stay at this index when
            // the occupant changed, so the component shifted into it is still told.
            if (index < _components.Count && ReferenceEquals(_components[index], component))
            {
                index++;
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

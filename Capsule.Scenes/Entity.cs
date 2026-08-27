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

    /// <param name="position">
    /// Where the entity starts. <see cref="PreviousPosition"/> starts equal to it, so a spawn
    /// does not slide in from wherever the renderer would otherwise interpolate from.
    /// </param>
    protected Entity(Vector2 position)
    {
        Position = position;
        PreviousPosition = position;
    }

    /// <summary>Where the entity is now, in world units.</summary>
    public Vector2 Position { get; set; }

    /// <summary>
    /// <see cref="Position"/> as of the previous step. Engine-managed: the scene retains it at
    /// the top of every step, and the renderer interpolates the pair by the frame alpha.
    /// </summary>
    public Vector2 PreviousPosition { get; internal set; }

    /// <summary>The scene holding this entity; null before it is added and after it is removed.</summary>
    public Scene? Scene { get; internal set; }

    internal ReadOnlySpan<Component> Components => CollectionsMarshal.AsSpan(_components);

    /// <summary>Moves immediately, with no interpolation from the old position.</summary>
    public void Teleport(Vector2 position)
    {
        Position = position;
        PreviousPosition = position;
    }

    /// <summary>Attaches <paramref name="component"/>, which no entity may already own.</summary>
    /// <exception cref="InvalidOperationException">The component is already attached to an entity.</exception>
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

        // Attaching to an entity a scene already holds changes that scene's renderer set.
        Scene?.InvalidateRenderers();
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

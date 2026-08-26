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

    public Vector2 Position { get; set; }

    /// <summary>
    /// <see cref="Position"/> as of the previous step. Engine-managed: the scene retains it at
    /// the top of every step, and the renderer interpolates the pair by the frame alpha.
    /// </summary>
    public Vector2 PreviousPosition { get; internal set; }

    /// <summary>The scene holding this entity; null before it is added and after it is removed.</summary>
    public Scene? Scene { get; internal set; }

    internal ReadOnlySpan<Component> Components => CollectionsMarshal.AsSpan(_components);

    /// <summary>Attaches <paramref name="component"/>, which no entity may already own.</summary>
    /// <exception cref="InvalidOperationException">The component is already attached to an entity.</exception>
    public void Add(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.Entity is not null)
        {
            throw new InvalidOperationException(
                $"A {component.GetType().Name} is already attached to a {component.Entity.GetType().Name}; a component belongs to one entity for its lifetime.");
        }

        component.Entity = this;
        _components.Add(component);

        // Attaching to an entity a scene already holds changes that scene's renderer set.
        Scene?.InvalidateRenderers();
    }

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

    // Count is read each iteration: a component attached by an earlier one updates this step
    // rather than waiting for the next.
    internal void UpdateComponents(in StepContext context)
    {
        for (int i = 0; i < _components.Count; i++)
        {
            _components[i].Update(context);
        }
    }
}

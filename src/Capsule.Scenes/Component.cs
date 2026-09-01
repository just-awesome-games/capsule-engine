namespace Capsule.Scenes;

/// <summary>
/// A slot of behaviour or appearance on one <see cref="Scenes.Entity"/>. Updated after its
/// entity, in the order it was attached.
/// </summary>
public abstract class Component
{
    /// <summary>The entity this component is attached to; null until it is attached.</summary>
    public Entity? Entity { get; internal set; }

    /// <summary>Whether the entity holding this component is currently in a scene.</summary>
    protected bool InScene { get; private set; }

    /// <summary>Advances this component by one fixed step.</summary>
    public virtual void Update(in StepContext context)
    {
    }

    /// <summary>
    /// Runs once the component's entity is in a scene, with <see cref="Entity"/> and its
    /// <see cref="Scenes.Entity.Scene"/> both set. Attaching to an entity a scene already holds
    /// runs it immediately. Whatever the component registers with that scene is registered here.
    /// </summary>
    protected internal virtual void OnAddedToScene()
    {
    }

    /// <summary>
    /// Runs once the component's entity is no longer in a scene, or once it is detached from an
    /// entity that was. Anything registered in <see cref="OnAddedToScene"/> is released here.
    /// </summary>
    protected internal virtual void OnRemovedFromScene()
    {
    }

    /// <summary>
    /// Runs once <paramref name="entity"/> holds this component. Whatever the component registers
    /// with its entity — an interest in its movement, say — is registered here.
    /// </summary>
    internal virtual void OnAttachedTo(Entity entity)
    {
    }

    /// <summary>
    /// Runs as the component leaves its entity, releasing what <see cref="OnAttachedTo"/>
    /// registered.
    /// </summary>
    internal virtual void OnDetachingFrom(Entity entity)
    {
    }

    /// <summary>Runs whenever the entity's position is written, including a teleport.</summary>
    internal virtual void OnEntityMoved()
    {
    }

    // Idempotent on both sides: an entity notifies its components when it joins a scene, and
    // Entity.Add notifies one attached to an entity that is already in one. Without the flag a
    // component attached from inside another's OnAddedToScene would be notified twice.
    internal void EnterScene()
    {
        if (InScene)
        {
            return;
        }

        InScene = true;
        OnAddedToScene();
    }

    internal void LeaveScene()
    {
        if (!InScene)
        {
            return;
        }

        InScene = false;
        OnRemovedFromScene();
    }
}

using System.Numerics;

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
    /// Runs before this component registers anything with <paramref name="scene"/>, and before any
    /// sibling on <paramref name="entity"/> has registered either. Everything registration needs is
    /// refused by throwing from here, while the entity is still outside the scene. Nothing may be
    /// published or claimed: a refusal from any component has to leave the scene as it was, and it
    /// is the admission as a whole — once every component has accepted — that claims what it needs.
    /// <para>
    /// Collision tag names the component would have to intern go into <paramref name="tags"/>
    /// rather than being checked against the world's remaining capacity here — every component
    /// being admitted adds to the same list, and the room for all of them is judged once, so two
    /// siblings each wanting the last free slot are refused together rather than one at a time.
    /// </para>
    /// </summary>
    internal virtual void OnAddingTo(Scene scene, Entity entity, List<string> tags)
    {
    }

    /// <summary>
    /// Runs once the component's entity is in a scene, with <see cref="Entity"/> and its
    /// <see cref="Scenes.Entity.Scene"/> both set. Attaching to an entity a scene already holds
    /// runs it immediately. Every component has already accepted the scene through
    /// <see cref="OnAddingTo"/>, and what they said they needed has been claimed since, so this
    /// cannot refuse the scene and cannot be beaten to it by a sibling's hook running first.
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
    /// Runs before <paramref name="entity"/> takes hold of this component, and before anything
    /// about either has changed. A component that cannot live on that entity — at the position it
    /// currently stands at — throws from here, and the attach leaves both untouched. Nothing may be
    /// registered here: a later preflight can still refuse the attach, and what this one put in
    /// place would have nobody to take it out again.
    /// </summary>
    internal virtual void OnAttachingTo(Entity entity)
    {
    }

    /// <summary>
    /// Runs once <paramref name="entity"/> holds this component and every preflight has passed.
    /// Whatever the component registers with its entity — an interest in its movement, say — is
    /// registered here, where nothing left can refuse the attach.
    /// </summary>
    internal virtual void OnAttachedTo(Entity entity)
    {
    }

    /// <summary>
    /// Runs as the component leaves its entity, releasing what <see cref="OnAttachedTo"/>
    /// registered. <see cref="OnAttachingTo"/> registers nothing, so there is nothing of its to
    /// release.
    /// </summary>
    internal virtual void OnDetachingFrom(Entity entity)
    {
    }

    /// <summary>
    /// Runs before the entity's position is written, on every component it holds, with the position
    /// it is about to take. A component that could not follow the entity there throws from here,
    /// while the entity and every one of its siblings are still untouched. Implementations must
    /// mutate nothing: whichever of them throws, the others have already been asked.
    /// </summary>
    internal virtual void OnEntityMoving(Vector2 position)
    {
    }

    /// <summary>
    /// Runs whenever the entity's position is written, including a teleport. Every component has
    /// already accepted the position through <see cref="OnEntityMoving"/>, so this cannot refuse it.
    /// </summary>
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

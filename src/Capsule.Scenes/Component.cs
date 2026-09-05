namespace Capsule.Scenes;

/// <summary>
/// A slot of behaviour or appearance on one <see cref="Scenes.Entity"/>. Stepped after its
/// entity, in the order it was attached.
/// </summary>
public abstract class Component
{
    private bool _started;

    /// <summary>The entity this component is attached to; null until it is attached.</summary>
    public Entity? Entity { get; internal set; }

    /// <summary>Whether the entity holding this component is currently in a scene.</summary>
    protected bool InScene { get; private set; }

    /// <summary>
    /// The run's deterministic random source, reached through the scene. This is the default
    /// stream; a domain whose draws must not move another's takes its own —
    /// <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This component is on no entity, is on one in no scene, or its scene has not started;
    /// randomness is discovered in <see cref="OnStart"/>. <see cref="OnAddedToScene"/> reaches it only when the
    /// scene had already started before this was added.
    /// </exception>
    public RandomSource Random => Entity?.Scene is { } scene
        ? scene.Random
        : throw new InvalidOperationException($"{GetType().Name} is on no entity in a scene, so {Scenes.Scene.NoSourceYet}");

    /// <summary>
    /// Advances this component by one fixed step, after its entity has stepped. Never reached
    /// before <see cref="OnStart"/>: a component that has not started takes no step.
    /// </summary>
    protected internal virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Runs once, before this component's first step and after everything added alongside it: its
    /// entity has started and is in a scene, so that scene may be searched from here. Attaching to
    /// an entity that has already started and is in a scene runs it immediately; attaching to one
    /// out of a scene, or to one queued to leave the scene it is in, waits until that entity is in
    /// a scene again.
    /// </summary>
    protected internal virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once the component's entity is in a scene, with <see cref="Entity"/> and its
    /// <see cref="Scenes.Entity.Scene"/> both set; attaching to an entity a scene already holds
    /// runs it immediately. Whatever the component registers with that scene is registered here;
    /// the scene's other contents may not exist yet, so discover them in <see cref="OnStart"/>.
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

    // Runs once entity holds this component. Whatever the component registers with its entity — an
    // interest in its movement, say — is registered here.
    internal virtual void OnAttachedTo(Entity entity)
    {
    }

    // Runs as the component leaves its entity, releasing what OnAttachedTo registered.
    internal virtual void OnDetachingFrom(Entity entity)
    {
    }

    // Runs whenever the entity's position is written, including a teleport.
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

    // Idempotent for the same reason EnterScene is.
    internal void RunStart()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        OnStart();
    }

    // Nothing steps before it has started. A component taken on by an entity that could not start
    // it — one queued to leave the scene — is held and stepped over until the add that starts it.
    internal void RunStep(in StepContext context)
    {
        if (!_started)
        {
            return;
        }

        OnStep(context);
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

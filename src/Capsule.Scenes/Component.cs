using Capsule.Assets;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

/// <summary>
/// A slot of behaviour or appearance on one <see cref="Scenes.Entity"/> — a renderer, a collider,
/// an animator, or a game's own. It owns no place of its own: it reads its entity's position and
/// the scene through it. Stepped after its entity, in attachment order. Override
/// <see cref="OnStart"/> to find what it needs, <see cref="OnStep"/> to advance it,
/// <see cref="OnDebugPanel"/> to expose it to the overlay, and <see cref="CollectAssets"/> to
/// declare what it loads.
/// </summary>
public abstract class Component
{
    private bool _started;

    /// <summary>The entity this component is attached to; null until it is attached.</summary>
    public Entity? Entity { get; internal set; }

    /// <summary>Whether the entity holding this component is currently in a scene.</summary>
    protected bool InScene { get; private set; }

    /// <summary>The run of the scene holding this component's entity.</summary>
    /// <exception cref="InvalidOperationException">
    /// This component is on no entity, is on one in no scene, or its scene has not started; reach
    /// the run from <see cref="OnStart"/> on.
    /// </exception>
    public Run Run => Entity?.Scene is { } scene
        ? scene.Run
        : throw new InvalidOperationException($"{GetType().Name} is on no entity in a scene, so {Scene.NoRunYet}");

    /// <summary>
    /// The run's default deterministic random stream. A domain whose draws must not move another's
    /// takes its own — <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This component is on no entity, is on one in no scene, or its scene has not started; reach
    /// it from <see cref="OnStart"/> on.
    /// </exception>
    public RandomSource Random => Run.Random;

    /// <summary>
    /// Advances this component by one fixed step, after its entity has stepped. Never reached
    /// before <see cref="OnStart"/>: a component that has not started takes no step.
    /// </summary>
    protected internal virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Advances this component a second time, after its entity's late step and in attachment order,
    /// once every entity has stepped and contacts have settled. Never reached before
    /// <see cref="OnStart"/>.
    /// </summary>
    protected internal virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>
    /// Draws this component's debug geometry through <see cref="Diagnostics.DebugDraw"/>. Called
    /// once per fixed step after the step has fully settled — every position, contact and the
    /// camera's framing are final — and only while a development overlay is attached, never in a
    /// shipping build's runtime, whose compile-out leaves the override costing nothing. Draw only:
    /// state changed here makes a run with the overlay differ from one without.
    /// </summary>
    protected internal virtual void OnDebugDraw()
    {
    }

    /// <summary>
    /// Fills this component's section of the development overlay's panel through
    /// <paramref name="panel"/>: a <see cref="DebugPanel.Field(string, string?)"/> per value worth
    /// reading, a <see cref="DebugPanel.Command"/> or <see cref="DebugPanel.Toggle"/> per thing
    /// worth doing to it. The section is headed by the component's type name and shows its fields
    /// first, then its commands and toggles under a <c>Commands</c> sub-heading, in write order
    /// within each group; a command or toggle runs inside the one stepped tick that follows it,
    /// ahead of the scene's own step. Called only while the overlay is showing this component's
    /// entity, never before <see cref="OnStart"/>, and never in a shipping build's runtime, whose
    /// compile-out leaves the override costing nothing. Write only: state changed here, outside a
    /// command, makes a run whose panel was opened differ from one whose was not.
    /// </summary>
    protected internal virtual void OnDebugPanel(DebugPanel panel)
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

    /// <summary>
    /// Appends assets this component declares. Collection may happen before <see cref="OnStart"/>,
    /// so declarations use construction-time state only. Override only to append declarations to
    /// <paramref name="assets"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is null.</exception>
    protected internal virtual void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
    }

    // Whether this component reads or reports its entity's authored position as where it is on
    // screen — a collider, a body, a screen notifier — and so refuses an entity a scroll factor
    // draws elsewhere.
    internal virtual bool AnswersInAuthoredSpace => false;

    // Whatever the component registers with its entity — an interest in its movement, say — is
    // registered here.
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

    // Runs at the top of every step, as the scene retains its entities' previous positions: a
    // component with interpolated state of its own retains its previous value here, so the frame
    // after this step interpolates from what the step began with.
    internal virtual void Retain()
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

    // Bound by the same rule RunStep is.
    internal void RunLateStep(in StepContext context)
    {
        if (!_started)
        {
            return;
        }

        OnLateStep(context);
    }

    internal void RunDebugDraw()
    {
        if (_started)
        {
            OnDebugDraw();
        }
    }

    internal void RunDebugPanel(DebugPanel panel)
    {
        if (_started)
        {
            OnDebugPanel(panel);
        }
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

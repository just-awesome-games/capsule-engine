using Capsule.Assets;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

/// <summary>
/// One capability attached to an <see cref="Scenes.Entity"/>, such as a renderer, a collider, an
/// animator, or a game's own. Make something a component when another entity type would attach it
/// unchanged. A component has no position of its own and reads its entity's position and scene through
/// the entity. The scene steps components after their entity, in attachment order. Override
/// <see cref="OnStart"/> to find what it needs, <see cref="OnStep"/> to advance it,
/// <see cref="OnDebugPanel"/> to expose it to the overlay, and <see cref="CollectAssets"/> to declare
/// what it loads.
/// </summary>
public abstract class Component
{
    private bool _started;

    /// <summary>The entity this component is attached to, or null until it is attached.</summary>
    public Entity? Entity { get; internal set; }

    /// <summary>Whether the entity holding this component is currently in a scene.</summary>
    protected bool InScene { get; private set; }

    /// <summary>The run of the scene holding this component's entity.</summary>
    /// <exception cref="InvalidOperationException">
    /// This component is on no entity, is on an entity in no scene, or its scene has not started. Read
    /// the run from <see cref="OnStart"/> onwards.
    /// </exception>
    public Run Run => Entity?.SceneOrNull is { } scene
        ? scene.Run
        : throw new InvalidOperationException($"{GetType().Name} is on no entity in a scene, so {Scene.NoRunYet}");

    /// <summary>
    /// The run's default deterministic random stream. A domain whose draws must not disturb another takes
    /// its own stream, as <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </summary>
    public RandomSource Random => Run.Random;

    /// <summary>
    /// Advances this component by one fixed step, after its entity has stepped. A component steps only
    /// after <see cref="OnStart"/> has run.
    /// </summary>
    protected internal virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Advances this component a second time, after its entity's late step and in attachment order, once
    /// every entity has stepped and contacts have settled. The scene calls this only after
    /// <see cref="OnStart"/>.
    /// </summary>
    protected internal virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>
    /// Draws this component's debug geometry through <see cref="Diagnostics.DebugDraw"/>. Called once per
    /// fixed step after the step has fully settled, with every position, contact and the camera's framing
    /// final, and only while a development overlay is attached. Draw here and change nothing, or a run
    /// with the overlay will behave differently from one without it.
    /// </summary>
    protected internal virtual void OnDebugDraw()
    {
    }

    /// <summary>
    /// Fills this component's section of the development overlay's panel. Write one
    /// <see cref="DebugPanel.Field(string, string?)"/> per value worth reading, and one
    /// <see cref="DebugPanel.Command"/> or <see cref="DebugPanel.Toggle"/> per action worth offering. The
    /// section is headed by the component's type name and shows fields first, then commands and toggles,
    /// in write order within each group. A command or toggle runs inside the next stepped tick, ahead of
    /// the scene's own step. Called only while the overlay is showing this component's entity, and only
    /// after <see cref="OnStart"/>. Write the panel and change nothing outside a command, or a run
    /// whose panel was opened will behave differently from one whose panel was not.
    /// </summary>
    protected internal virtual void OnDebugPanel(DebugPanel panel)
    {
    }

    /// <summary>
    /// Runs once, before this component's first step and after everything added alongside it. Its entity
    /// has started and is in a scene by then, so the scene can be searched from here. Attaching to an
    /// entity that has already started and is in a scene runs this immediately. Attaching to an entity out
    /// of a scene, or one queued to leave, waits until that entity is in a scene again.
    /// </summary>
    protected internal virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once the component's entity is in a scene, with <see cref="Entity"/> and its
    /// <see cref="Scenes.Entity.Scene"/> both set. Attaching to an entity a scene already holds runs this
    /// immediately. Register with the scene here. The scene's other contents may not exist yet, so find
    /// them in <see cref="OnStart"/>.
    /// </summary>
    protected internal virtual void OnAddedToScene()
    {
    }

    /// <summary>
    /// Runs once the component's entity is no longer in a scene, or once the component is detached from an
    /// entity that was. Release anything registered in <see cref="OnAddedToScene"/> here.
    /// </summary>
    protected internal virtual void OnRemovedFromScene()
    {
    }

    /// <summary>
    /// Appends assets this component declares. Collection can run before <see cref="OnStart"/>, so
    /// declare from construction-time state. An override appends to <paramref name="assets"/> and
    /// changes nothing else.
    /// </summary>
    protected internal virtual void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
    }

    // Which parts of its entity's transform this component supports. Position alone means the component
    // follows world position and answers in authored space, so it rejects a scroll factor and any turn or
    // scale up the ancestry. Scale means it can be resized but not turned, and Full accepts everything.
    // The entity throws on whichever comes second, the transform write or the attach.
    internal virtual TransformSupport Supports => TransformSupport.Full;

    // Registers whatever the component needs from its entity, such as an interest in its movement.
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

    // Runs at the top of every step, while the scene saves its entities' previous positions. A component
    // with interpolated state of its own saves its previous value here, so the frame after this step
    // interpolates from the value the step began with.
    internal virtual void SavePrevious()
    {
    }

    // Safe to call twice. An entity notifies its components when it joins a scene, and Entity.Add notifies
    // a component attached to an entity already in one. Without the flag, a component attached from inside
    // another's OnAddedToScene would be notified twice.
    internal void EnterScene()
    {
        if (InScene)
        {
            return;
        }

        InScene = true;
        OnAddedToScene();
    }

    // Safe to call twice, for the same reason EnterScene is.
    internal void RunStart()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        OnStart();
    }

    // A component that has not started does not step. A component attached to an entity that could not
    // start it, such as one queued to leave the scene, is skipped until a later add starts it.
    internal void RunStep(in StepContext context)
    {
        if (!_started)
        {
            return;
        }

        OnStep(context);
    }

    // Same rule as RunStep: a component that has not started takes no late step.
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

// The parts of an entity's transform a component can sit under. Every component follows Position.
[Flags]
internal enum TransformSupport
{
    Position = 0,
    Scale = 1,
    Rotation = 2,
    Full = Scale | Rotation,
}

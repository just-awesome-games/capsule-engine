using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// One thing in a scene. World units by default, Y-down; what <see cref="Position"/> anchors — a
/// corner, a centre, a pair of feet — is the subclass's own convention. Subclass it for behaviour and
/// attach <see cref="Component"/>s for what composes.
/// <para>
/// An entity whose <see cref="Space"/> is <see cref="RenderSpace.Screen"/> lives in canvas pixels
/// from its <see cref="Anchor"/> instead, and every renderer it holds draws on the frame's screen
/// layer, over the whole world.
/// </para>
/// </summary>
public class Entity
{
    private readonly List<Component> _components = [];

    private int _movementTrackers;
    private bool _started;

    /// <param name="position">
    /// Where the entity starts. <see cref="PreviousPosition"/> starts equal to it, so a spawn does
    /// not slide in from wherever the renderer would otherwise interpolate from.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    protected Entity(Vector2 position)
    {
        Position = position;
        PreviousPosition = position;
    }

    /// <summary>
    /// Where the entity is now, in world units. Always finite: a NaN or infinite position is
    /// refused rather than stored.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    /// <exception cref="ArgumentException">A tracked collider cannot be placed there; nothing moves.</exception>
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

            Vector2 previous = field;
            field = value;

            if (_movementTrackers == 0)
            {
                return;
            }

            // A collider may refuse the placement (its shape overflows there). Every collider is
            // then returned to the old position, which each already held, so nothing is left stale.
            try
            {
                NotifyMoved();
            }
            catch
            {
                field = previous;
                NotifyMoved();
                throw;
            }
        }
    }

    /// <summary>
    /// <see cref="Position"/> as of the previous step. Engine-managed: the scene retains it at
    /// the top of every step, and the renderer interpolates the pair by the frame alpha — a screen
    /// entity exactly as a world one.
    /// </summary>
    public Vector2 PreviousPosition { get; internal set; }

    /// <summary>
    /// Which of a frame's two layers this entity's renderers draw on, and so what
    /// <see cref="Position"/> means: world units under <see cref="RenderSpace.World"/>, the default,
    /// and canvas pixels from <see cref="Anchor"/> under <see cref="RenderSpace.Screen"/>. Every
    /// renderer the entity holds follows it, with no flag of its own, and the whole screen layer draws
    /// over the whole world layer however the two are banded.
    /// </summary>
    public RenderSpace Space { get; set; }

    /// <summary>
    /// The point on the canvas <see cref="Position"/> is measured from, as a fraction of the canvas on
    /// each axis; <see cref="Scenes.Anchor.TopLeft"/> by default. Read only in
    /// <see cref="RenderSpace.Screen"/>, and ignored in world space.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A fraction is not finite.</exception>
    public Anchor Anchor
    {
        get;

        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "An anchor is a finite fraction of the canvas on each axis.");
            }

            field = value;
        }
    }

    /// <summary>
    /// The band this entity draws in: an ordering key, never a coordinate, and nothing else reads
    /// it. Each attached <see cref="Rendering.Renderer"/> draws at this plus its own
    /// <see cref="Rendering.Renderer.ZIndex"/>, summed as a <see cref="long"/> with neither side
    /// clamped, and the higher sum draws later. Renderers whose sums are equal keep entity
    /// insertion order and then attachment order. Zero by default. Written from inside a
    /// <see cref="Rendering.Renderer.Draw"/> — like any change to what the scene holds — it orders
    /// the next step's frame rather than the one being drawn.
    /// </summary>
    public int ZIndex
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Scene?.InvalidateRenderers();
        }
    }

    /// <summary>The scene holding this entity; null before it is added and after it is removed.</summary>
    public Scene? Scene { get; internal set; }

    /// <summary>
    /// The run's deterministic random source, reached through the scene. This is the default
    /// stream; a domain whose draws must not move another's takes its own —
    /// <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This entity is in no scene, or its scene has not started; randomness is discovered in
    /// <see cref="OnStart"/>. <see cref="OnAddedToScene"/> reaches it only when the
    /// scene had already started before this was added.
    /// </exception>
    public RandomSource Random => Scene is { } scene
        ? scene.Random
        : throw new InvalidOperationException($"{GetType().Name} is in no scene, so {Scene.NoSourceYet}");

    // What a renderer adds to a position to reach the space it draws in: the anchor's point on the
    // run's canvas for a screen entity, and nothing at all in world space.
    internal Vector2 SpaceOrigin =>
        Space == RenderSpace.Screen ? Anchor.On(Scene?.Canvas ?? Vector2.Zero) : Vector2.Zero;

    // Set by a subclass whose contents are world coordinates, so a position write is a mistake
    // rather than a move.
    internal bool Anchored { get; init; }

    internal ReadOnlySpan<Component> Components => CollectionsMarshal.AsSpan(_components);

    // Every walk of the component list goes through this. A hook may detach the component being
    // visited or one before it, which shifts the rest left; the cursor holds its index when the
    // occupant changed, so the component shifted into it is visited rather than skipped.
    private ComponentWalk LiveComponents => new(_components);

    /// <summary>Moves immediately, with no interpolation from the old position.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    /// <exception cref="ArgumentException">A tracked collider cannot be placed there; nothing moves.</exception>
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
    /// <exception cref="ArgumentNullException">The component is null.</exception>
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

        // Time has begun for this entity, so it has begun for whatever it takes on. Both conditions
        // are re-read after the hooks above, either of which may have detached the component or
        // taken this entity out of the scene. Kept, not merely held: an entity queued for removal
        // still names its scene but never steps again, so its new component waits for the next add.
        if (_started && Scene?.Keeps(this) == true && ReferenceEquals(component.Entity, this))
        {
            component.RunStart();
        }
    }

    /// <summary>Detaches <paramref name="component"/> so it may be attached elsewhere.</summary>
    /// <exception cref="InvalidOperationException">The component is not attached to this entity.</exception>
    /// <exception cref="ArgumentNullException">The component is null.</exception>
    public void Remove(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (!ReferenceEquals(component.Entity, this))
        {
            throw new InvalidOperationException(
                $"A {component.GetType().Name} that this entity does not hold cannot be removed from it.");
        }

        _components.RemoveAt(Scene.IndexOf(_components, component));

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
    /// from here. Runs before the components held at that moment start; an entity that leaves the
    /// scene from here never steps, and so starts none of them.
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

    /// <summary>
    /// Appends assets this entity declares beyond those owned by its components. Collection may
    /// happen before <see cref="OnStart"/>, so declarations use construction-time state only.
    /// Override only to append declarations to <paramref name="assets"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is null.</exception>
    protected internal virtual void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
    }

    internal void CollectAssetPreloads(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        CollectAssets(assets);

        foreach (Component component in Components)
        {
            component.CollectAssets(assets);
        }
    }

    // Counts the components that want telling when this entity moves.
    internal void TrackMovement(int delta) => _movementTrackers += delta;

    // The entity's own start is once for its lifetime; the component sweep is not. An entity
    // removed and added again reaches this holding components that never started.
    internal void RunStart()
    {
        if (!_started)
        {
            _started = true;
            OnStart();
        }

        // OnStart may have taken this entity out of the scene, or queued it to leave at the end of
        // the drain. Either way it never steps, so its components must not start: a component's
        // OnStart is promised a scene to search.
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

    // Nothing steps before it has started: an entity the scene holds but never started has no time
    // begun for it, so neither it nor anything it holds may be advanced.
    internal void RunStep(in StepContext context)
    {
        if (!_started)
        {
            return;
        }

        OnStep(context);

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

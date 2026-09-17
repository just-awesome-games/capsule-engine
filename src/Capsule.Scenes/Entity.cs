using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes.Lifecycle;
using Capsule.Scenes.Spawning;
using Capsule.UI;

namespace Capsule.Scenes;

/// <summary>
/// One thing in a scene, in world units, Y-down; what <see cref="Position"/> anchors — a corner, a
/// centre, a pair of feet — is the subclass's own convention. An entity is what a thing is:
/// behaviour that is its identity — its state machine, its phases, its spin — lives on the
/// subclass, and its step is one ordered method; a capability things share is a
/// <see cref="Component"/> attached to it. The test for the subclass: is this what the entity is?
/// A bare entity constructed under a <see cref="Parent"/> is a marker or a pivot with a place and
/// no behaviour.
/// <para>
/// Under a parent, <see cref="Position"/>, <see cref="Rotation"/> and <see cref="Scale"/> are local
/// to it and <see cref="WorldTransform"/> is what the scene sees. Only presentation honours a turn
/// or a scale; anything that collides follows position alone. <see cref="ScreenEntity"/> is the
/// interface counterpart, on the frame's screen layer.
/// </para>
/// </summary>
public partial class Entity
{
    // The half-length of the origin cross's arms, in world units.
    private const float OriginArm = 1.5f;

    private readonly List<Component> _components = [];

    // Allocated by the first child: most entities have none.
    private List<Entity>? _children;
    private Entity? _parent;

    // The top of this entity's chain, itself for a root: what the layer, its origin and the scroll
    // factor are read from, rewritten down the subtree where a parent is set or let go.
    private Entity _root;

    // Colliders in this entity's subtree, its own included: a write walks down to re-place them
    // and skips a branch holding none.
    private int _movementTrackers;
    private bool _started;
    private Vector2 _scrollFactor = Vector2.One;

    /// <summary>
    /// A marker or pivot under <paramref name="parent"/>, at its origin: it has a place in the
    /// world and no behaviour of its own. Joins the parent's scene as <see cref="Parent"/>
    /// describes.
    /// </summary>
    /// <param name="parent">The entity this one is placed by.</param>
    /// <exception cref="ArgumentNullException">The parent is null.</exception>
    /// <exception cref="InvalidOperationException">The parent refuses this entity, on <see cref="Parent"/>'s terms.</exception>
    public Entity(Entity parent)
        : this(parent, Vector2.Zero)
    {
    }

    /// <summary>
    /// A marker or pivot under <paramref name="parent"/>, at <paramref name="position"/> in the
    /// parent's own space, as <see cref="Entity(Entity)"/> otherwise describes. A child with
    /// components or behaviour of its own is a nested subclass on its parent, taking its parent as
    /// the narrowest type it needs — <see cref="Entity"/> when it only rides, the concrete type
    /// when it observes — and what it needs through its constructor, never the outer class's
    /// state; a bare point is an <see cref="Entity"/> with a <see cref="Name"/>.
    /// </summary>
    /// <param name="parent">The entity this one is placed by.</param>
    /// <param name="position">Where the entity starts, local to the parent.</param>
    /// <exception cref="ArgumentNullException">The parent is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    /// <exception cref="InvalidOperationException">The parent refuses this entity, on <see cref="Parent"/>'s terms.</exception>
    public Entity(Entity parent, Vector2 position)
        : this(position)
    {
        ArgumentNullException.ThrowIfNull(parent);

        Parent = parent;
    }

    /// <param name="position">
    /// Where the entity starts. <see cref="PreviousTransform"/> starts equal to it, so a spawn does
    /// not slide in from wherever the renderer would otherwise interpolate from.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    protected Entity(Vector2 position)
    {
        _root = this;
        Position = position;
        _previousLocal = _local;
        _previousWorld = _local;
    }

    /// <summary>
    /// Starts from a document placement: <see cref="EntitySpawn.Position"/> as
    /// <see cref="Entity(Vector2)"/> takes it, then <see cref="ZIndex"/> and
    /// <see cref="ScrollFactor"/> where the spawn carries them, all before the derived constructor's
    /// body runs, so whatever that body writes wins over the document. <see cref="EntitySpawn.Scale"/>
    /// is left to that body. An entity whose own anchor is not the authored coordinate passes
    /// <c>spawn with { Position = spawn.Position + anchor }</c>. A class may hold this constructor
    /// beside a public one taking a <see cref="Vector2"/> for placement from code; only the one
    /// taking a spawn claims the document's spawn type.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The position or the scroll factor is not finite.</exception>
    /// <exception cref="InvalidOperationException">The scroll factor is not one on an entity that refuses one.</exception>
    protected Entity(EntitySpawn spawn)
        : this(spawn.Position)
    {
        if (spawn.ZIndex is { } band)
        {
            ZIndex = band;
        }

        if (spawn.ScrollFactor is { } factor)
        {
            ScrollFactor = factor;
        }
    }

    /// <summary>
    /// The entity this one is placed by, or null for a root. Setting it keeps <see cref="Position"/>,
    /// <see cref="Rotation"/> and <see cref="Scale"/> as they are, now local to the parent. Set
    /// under a parent a scene holds, the entity joins that scene as <see cref="Scene.Add"/> would;
    /// it leaves with the parent, and <see cref="Scene.Remove"/> on the entity alone clears this.
    /// Only an entity in no scene and queued for none may take a parent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This entity is in a scene or queued to join one; the parent is this entity or one of its
    /// descendants; this entity is a <see cref="ScreenEntity"/>, a <see cref="Tiles.TileMap"/> or
    /// carries a <see cref="ScrollFactor"/> other than one; the parent's scroll factor is not one
    /// over a subtree that collides or watches the screen; or an entity from the parent up is
    /// turned or scaled over a subtree holding a component that refuses it, on
    /// <see cref="Rotation"/>'s and <see cref="Scale"/>'s terms.
    /// </exception>
    public Entity? Parent
    {
        get => _parent;

        set
        {
            if (ReferenceEquals(value, _parent))
            {
                return;
            }

            if (Scene is not null || PendingScene is not null)
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} in a scene keeps its parent; remove it from the scene before parenting it elsewhere.");
            }

            if (value is not null)
            {
                RequireParentable(value);
            }

            Unlink();
            _parent = value;

            if (value is not null)
            {
                (value._children ??= []).Add(this);
                value.TrackMovement(_movementTrackers);
            }

            Invalidate(previous: true);
            value?.Scene?.Enqueue(this);
        }
    }

    /// <summary>
    /// The entities this one places, in the order they were parented. Invalidated by the next
    /// change to what this entity holds.
    /// </summary>
    public ReadOnlySpan<Entity> Children => _children is null ? default : CollectionsMarshal.AsSpan(_children);

    /// <summary>
    /// A label for the developer's eyes: what the overlay shows in place of the type name. Null by
    /// default, read by nothing but diagnostics, and need not be unique.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The band this entity draws in: an ordering key, never a coordinate, and nothing else reads
    /// it. Relative to the <see cref="Parent"/>'s: each attached <see cref="Rendering.Renderer"/>
    /// draws at the sum of this band up the ancestry plus its own
    /// <see cref="Rendering.Renderer.ZIndex"/>, as a <see cref="long"/> with nothing clamped, and
    /// the higher sum draws later. Renderers whose sums are equal keep the scene's entity order —
    /// tree order — and then attachment order. Zero by default. Written from inside a
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

    /// <summary>
    /// How far this entity's renderers move with the camera, per axis: one, the default, is the
    /// world; zero is fixed to the screen; less than one is further away; more than one is nearer.
    /// The root's alone: a child reads its root's factor, and writing one other than one on a child
    /// is refused. Presentation only, applied by the renderer to every renderer this entity and its
    /// descendants hold and read by nothing else: <see cref="Position"/>, colliders, contacts,
    /// <see cref="Rendering.Renderer.Bounds"/> and <see cref="OnDebugDraw"/> geometry stay in
    /// authored space. The camera corner at which every layer sits exactly where authored is its
    /// <see cref="Camera.ScrollOrigin"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A component of the factor is not finite.</exception>
    /// <exception cref="InvalidOperationException">
    /// The factor is not one on both axes and this entity has a parent, is on the screen layer, or
    /// collides or holds a <see cref="Rendering.VisibleOnScreenNotifier2D"/> anywhere in its subtree:
    /// each answers in authored space, where a scrolled entity is not drawn.
    /// </exception>
    public Vector2 ScrollFactor
    {
        get => _root._scrollFactor;

        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A scroll factor must be finite on both axes.");
            }

            if (value != Vector2.One)
            {
                if (_parent is not null)
                {
                    throw new InvalidOperationException(
                        $"A {GetType().Name} under a parent scrolls with its root; set the scroll factor on the root.");
                }

                if (Space == RenderSpace.Screen)
                {
                    throw new InvalidOperationException(
                        $"A {GetType().Name} is on the screen layer, which no camera moves, so it cannot carry a scroll factor.");
                }

                RequireScrollable();
            }

            _scrollFactor = value;
        }
    }

    /// <summary>The scene holding this entity; null before it is added and after it is removed.</summary>
    public Scene? Scene { get; internal set; }

    /// <summary>The run of the scene holding this entity.</summary>
    /// <exception cref="InvalidOperationException">
    /// This entity is in no scene, or its scene has not started; reach the run from
    /// <see cref="OnStart"/> on.
    /// </exception>
    public Run Run => Scene is { } scene
        ? scene.Run
        : throw new InvalidOperationException($"{GetType().Name} is in no scene, so {Scene.NoRunYet}");

    /// <summary>
    /// The run's default deterministic random stream. A domain whose draws must not move another's
    /// takes its own — <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This entity is in no scene, or its scene has not started; reach it from
    /// <see cref="OnStart"/> on.
    /// </exception>
    public RandomSource Random => Run.Random;

    // The scene this entity is queued to join, between the deferred add and the drain that lands
    // it; null otherwise. Held so a parent is refused while the add is in flight.
    internal Scene? PendingScene { get; set; }

    // Which of a frame's two layers this entity's renderers draw on: the root's, so a group under a
    // screen entity draws on the screen with it.
    internal RenderSpace Space => _root.OwnSpace;

    // What a renderer adds to a composed position to reach the space it draws in: nothing in world
    // space, and the anchor's point on the run's canvas under a screen entity root.
    internal Vector2 SpaceOrigin => _root.OwnSpaceOrigin;

    // The layer this entity would draw on as a root; ScreenEntity says the screen.
    internal virtual RenderSpace OwnSpace => RenderSpace.World;

    internal virtual Vector2 OwnSpaceOrigin => Vector2.Zero;

    // Set by a subclass whose contents are world coordinates, so a position write is a mistake
    // rather than a move.
    internal bool Anchored { get; init; }

    // Whether this entity registers a shape of its own with the collision world, beside any
    // collider component it holds: a tile map's grid.
    internal virtual bool Collides => false;

    internal ReadOnlySpan<Component> Components => CollectionsMarshal.AsSpan(_components);

    // ZIndex summed up the ancestry, written by the render index as it walks the scene in tree
    // order, so each parent's is summed before its children read it.
    internal long DrawBand { get; set; }

    // Every walk of the component list goes through this. A hook may detach the component being
    // visited or one before it, which shifts the rest left; the cursor holds its index when the
    // occupant changed, so the component shifted into it is visited rather than skipped.
    private ComponentWalk LiveComponents => new(_components);


    /// <summary>Attaches <paramref name="component"/>, which no entity may already own.</summary>
    /// <exception cref="InvalidOperationException">
    /// The component is already attached to an entity, or it refuses this entity — a
    /// <see cref="Physics.KinematicBody2D"/> offered to one that already holds a body; a
    /// collider, body or <see cref="Rendering.VisibleOnScreenNotifier2D"/> offered to one whose
    /// <see cref="ScrollFactor"/> is not one or that is scaled anywhere up its ancestry; or a
    /// component that cannot turn offered to one turned anywhere up its ancestry, the message
    /// naming the entity carrying the value.
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

        TransformSupport supports = component.Supports;
        if (supports == TransformSupport.Position && ScrollFactor != Vector2.One)
        {
            throw Unscrollable($"a {component.GetType().Name}");
        }

        for (Entity? above = this; above is not null; above = above._parent)
        {
            if ((supports & TransformSupport.Rotation) == 0 && above._local.Rotation != 0f)
            {
                throw Turned(component, this, above, above._local.Rotation);
            }

            if ((supports & TransformSupport.Scale) == 0 && above._local.Scale != Vector2.One)
            {
                throw Scaled(component, this, above, above._local.Scale);
            }
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

        _components.RemoveAt(ReferenceList.IndexOf(_components, component));

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
    /// Advances this entity by one fixed step, before its components step and before its
    /// <see cref="Children"/> step: the scene steps in tree order, so a child reads the world
    /// position its parent moved to this step. Never reached before <see cref="OnStart"/>: an
    /// entity the scene holds but has not started takes no step, and neither do the components it
    /// holds.
    /// </summary>
    protected internal virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Advances this entity a second time, after every entity has stepped and contacts have
    /// settled, so what is read here is what the frame about to be drawn will show. Runs before
    /// this entity's components' own late step, in the same order <see cref="OnStep"/> did, and
    /// before the scene's <see cref="Scene.OnLateStep"/>. Never reached before
    /// <see cref="OnStart"/>.
    /// </summary>
    protected internal virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>
    /// Draws this entity's debug geometry, as <see cref="Component.OnDebugDraw"/> describes;
    /// nothing by default. A world entity's origin cross is drawn at its <see cref="WorldPosition"/>
    /// before this is called and whatever this draws, so an override cannot lose it.
    /// </summary>
    protected internal virtual void OnDebugDraw()
    {
    }

    /// <summary>
    /// Fills this entity's own section of its panel — the one headed <c>Entity</c> — as
    /// <see cref="Component.OnDebugPanel"/> describes; nothing by default. <see cref="Name"/> where
    /// set, <see cref="Transform"/> — beside <see cref="WorldTransform"/> where the entity has a
    /// <see cref="Parent"/> — <see cref="ZIndex"/> and <see cref="ScrollFactor"/>, and a
    /// <c>Remove</c> command that takes the entity out of its scene, are written into that section
    /// before this is called, so an override cannot lose them, and each component fills its own
    /// section under its heading after it.
    /// </summary>
    protected internal virtual void OnDebugPanel(DebugPanel panel)
    {
    }

    /// <summary>
    /// Runs once for this entity's lifetime — not again when it is added to a scene a second time —
    /// before its first step and after everything added alongside it, so the scene may be searched
    /// from here; a parent starts before its children, and a subtree added together starts as one
    /// batch. Runs before the components held at that moment start; an entity that leaves the
    /// scene from here never steps, and so starts none of them.
    /// </summary>
    protected internal virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once the scene holds this entity, with <see cref="Scene"/> set, and before its
    /// <see cref="Children"/> join. Peers added alongside it may not exist yet: register with the
    /// scene here and discover it in <see cref="OnStart"/>.
    /// </summary>
    protected internal virtual void OnAddedToScene()
    {
    }

    /// <summary>
    /// Runs once the scene has let go of this entity, with <see cref="Scene"/> cleared — when the
    /// entity is removed, and when the scene stops — and after every descendant has left: a
    /// subtree leaves children first, deepest first. Anything <see cref="OnAddedToScene"/>
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

    // Counted on this entity and every ancestor, so a position write above knows whether any
    // branch below needs re-placing.
    internal void TrackMovement(int delta)
    {
        for (Entity? entity = this; entity is not null; entity = entity._parent)
        {
            entity._movementTrackers += delta;
        }
    }

    // Severs this entity from its parent as it leaves a scene on its own: the checks a parent
    // write runs do not apply to letting go.
    internal void Orphan()
    {
        Unlink();
        _parent = null;
        Invalidate(previous: true);
    }

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
        List<Exception>? failures = null;
        try
        {
            OnRemovedFromScene();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        foreach (Component component in LiveComponents)
        {
            try
            {
                component.LeaveScene();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        CleanupFailures.Throw(failures);
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

    // Bound by the same rule RunStep is: an entity with no time begun for it takes no late step.
    internal void RunLateStep(in StepContext context)
    {
        if (!_started)
        {
            return;
        }

        OnLateStep(context);

        foreach (Component component in LiveComponents)
        {
            component.RunLateStep(context);
        }
    }

    internal void RunDebugDraw()
    {
        if (!_started)
        {
            return;
        }

        if (Space == RenderSpace.World)
        {
            DrawOrigin();
        }

        OnDebugDraw();

        foreach (Component component in LiveComponents)
        {
            component.RunDebugDraw();
        }
    }

    // The innate rows are the engine's, written before any hook so no override can lose them;
    // the hooks are bound by the rule RunStep is. Every component gets its heading whether or not
    // it has started, so the panel still shows what the entity is made of.
    internal void RunDebugPanel(DebugPanel panel)
    {
        panel.Section("Entity");
        if (Name is { } name)
        {
            panel.Field("Name", name);
        }

        panel.Field("Transform", _local);
        if (_parent is not null)
        {
            panel.Field("World Transform", World);
        }

        panel.Field("ZIndex", ZIndex);
        panel.Field("ScrollFactor", ScrollFactor);
        panel.Command("Remove", () => Scene?.Remove(this));

        if (_started)
        {
            OnDebugPanel(panel);
        }

        foreach (Component component in LiveComponents)
        {
            panel.Section(component.GetType().Name);
            component.RunDebugPanel(panel);
        }
    }

    // Everything a parent write refuses, checked before anything is linked.
    private void RequireParentable(Entity parent)
    {
        if (Anchored)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} is anchored at the world origin and cannot be placed by a parent.");
        }

        if (OwnSpace == RenderSpace.Screen)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} is on the screen layer and is only ever a root; a plain entity under a screen entity draws on the screen with it.");
        }

        if (_scrollFactor != Vector2.One)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} carrying a scroll factor cannot be placed by a parent; the factor is the root's, so set it there.");
        }

        if (parent.ScrollFactor != Vector2.One)
        {
            RequireScrollable();
        }

        for (Entity? above = parent; above is not null; above = above._parent)
        {
            if (ReferenceEquals(above, this))
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} cannot be placed by itself or by one of its own descendants.");
            }

            RequireTurnable(above, above._local.Rotation);
            RequireScalable(above, above._local.Scale);
        }
    }

    // Refuses a scroll factor other than one over this subtree.
    private void RequireScrollable()
    {
        if (Collides)
        {
            throw Unscrollable("its grid");
        }

        if (FirstRefuser(TransformSupport.Scale) is var (component, _))
        {
            throw Unscrollable($"a {component.GetType().Name}");
        }
    }

    // Takes this entity out of its parent's children, on the parent's side alone.
    private void Unlink()
    {
        if (_parent is { } parent)
        {
            parent._children!.RemoveAt(ReferenceList.IndexOf(parent._children, this));
            parent.TrackMovement(-_movementTrackers);
        }
    }

    private InvalidOperationException Unscrollable(string what) =>
        new($"A {GetType().Name} cannot carry a scroll factor other than one with {what}, which answers at the authored position a scrolled entity is not drawn at.");

    // The Origins channel: a cross at the entity's world position, carrying the step's motion.
    // Engine-owned, drawn from the driver so no override can lose it.
    private void DrawOrigin()
    {
        Vector2 position = World.Position;
        Vector2 motion = position - _previousWorld.Position;
        DebugDraw.Line(DebugDraw.Origins, position - new Vector2(OriginArm, 0f), position + new Vector2(OriginArm, 0f), null, motion);
        DebugDraw.Line(DebugDraw.Origins, position - new Vector2(0f, OriginArm), position + new Vector2(0f, OriginArm), null, motion);
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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes.Spawning;
using Capsule.UI;

namespace Capsule.Scenes;

/// <summary>
/// One object in a scene, in world units with Y-down. <see cref="Position"/> anchors differ by
/// subclass. A subclass carries the behaviour, and shared capabilities attach as
/// <see cref="Component"/>.
/// <para>
/// Under a <see cref="Parent"/>, local <see cref="Position"/>, <see cref="Rotation"/> and
/// <see cref="Scale"/> combine to <see cref="WorldTransform"/>. Rotation and scale reach
/// presentation only. Collision follows position. <see cref="ScreenEntity"/> is the screen-layer
/// counterpart.
/// </para>
/// </summary>
public partial class Entity
{
    // The half-length of the origin cross's arms, in world units.
    private const float OriginArm = 1.5f;

    private readonly List<Component> _components = [];

    // The first child allocates the list, because most entities have no children.
    private List<Entity>? _children;
    private Entity? _parent;

    // The root of this entity's chain. Render space, space origin, and scroll factor are read from it.
    private Entity _root;

    // Count of colliders in this subtree, used to avoid redundant writes.
    private int _movementTrackers;
    private bool _started;
    private Vector2 _scrollFactor = Vector2.One;

    // The pool that owns this entity for life, or null when the entity was never built by one.
    internal IEntityPool? Pool { get; set; }

    // Whether this entity is idle in Pool right now, refused by Scene.Add until it is taken.
    internal bool IdleInPool { get; set; }

    /// <summary>
    /// A marker or pivot at <paramref name="parent"/>'s origin. It has a place in the world and no
    /// behaviour of its own. It joins the parent's scene as <see cref="Parent"/> describes.
    /// </summary>
    /// <param name="parent">The entity this one is placed by.</param>
    public Entity(Entity parent)
        : this(parent, Vector2.Zero)
    {
    }

    /// <summary>
    /// A marker or pivot under <paramref name="parent"/> at <paramref name="position"/> in parent
    /// space. A child with its own behaviour is a nested subclass on its parent.
    /// </summary>
    /// <param name="parent">The entity this one is placed by.</param>
    /// <param name="position">The starting position, local to the parent.</param>
    public Entity(Entity parent, Vector2 position)
        : this(position)
    {
        ArgumentNullException.ThrowIfNull(parent);

        Parent = parent;
    }

    /// <param name="position">
    /// The starting position. <see cref="PreviousTransform"/> equals it so spawns do not interpolate.
    /// </param>
    protected Entity(Vector2 position)
    {
        _root = this;
        Position = position;
        _previousLocal = _local;
        _previousWorld = _local;
    }

    /// <summary>
    /// Spawns from a document. The position, <see cref="ZIndex"/> and <see cref="ScrollFactor"/> are
    /// applied before the subclass constructor runs, so its writes override the document.
    /// <see cref="EntitySpawn.Scale"/> is left to the subclass. An entity with a different anchor
    /// adjusts the position: <c>spawn with { Position = spawn.Position + anchor }</c>.
    /// </summary>
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
    /// The entity this one is placed by, or null for a root. Reparenting keeps <see cref="Position"/>,
    /// <see cref="Rotation"/> and <see cref="Scale"/> local. An entity parented under one a scene
    /// holds joins that scene and leaves when the parent does. <see cref="Scene.Remove"/> on this
    /// entity by itself clears the parent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This entity is in a scene, the parent is this entity or a descendant, scroll factors conflict,
    /// or rotation/scale in the ancestry conflicts with components in this subtree.
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

            if (SceneOrNull is not null || PendingScene is not null)
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} is in a scene. Remove it from the scene before reparenting it.");
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
            value?.SceneOrNull?.Enqueue(this);
        }
    }

    /// <summary>
    /// The entities this one places, in parenting order. Invalidated by changes.
    /// </summary>
    public ReadOnlySpan<Entity> Children => _children is null ? default : CollectionsMarshal.AsSpan(_children);

    /// <summary>
    /// A developer-facing label the debug overlay shows in place of the type name. Defaults to null,
    /// only diagnostics read it, and duplicates are allowed.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The render band: an ordering key applied to all attached renderers. Each renderer's draw order
    /// is the sum of this band up the ancestry plus its own <see cref="Rendering.Renderer.ZIndex"/>.
    /// Higher sums draw later. Equal sums use tree order, then attachment order. Writes inside
    /// <see cref="Rendering.Renderer.Draw"/> affect the next frame.
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
            SceneOrNull?.InvalidateRenderers();
        }
    }

    /// <summary>
    /// How far this entity's renderers move with the camera, per axis. One is world speed, zero is
    /// fixed to the screen, below one is further away and above one is nearer. The root's factor
    /// applies to its whole subtree. This affects drawing only, so position, colliders and bounds
    /// stay in authored space. The camera's <see cref="Camera.ScrollOrigin"/> anchors every layer.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Non-one factors are forbidden on children, screen-layer entities, entities that collide, or
    /// entities holding <see cref="Rendering.VisibleOnScreenNotifier2D"/> in the subtree.
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
                        $"A {GetType().Name} has a parent and scrolls with its root; set the scroll factor on the root.");
                }

                if (Space == RenderSpace.Screen)
                {
                    throw new InvalidOperationException(
                        $"A {GetType().Name} is on the screen layer and cannot carry a scroll factor. Leave the factor at one.");
                }

                RequireScrollable();
            }

            _scrollFactor = value;
        }
    }

    /// <summary>The scene holding this entity.</summary>
    /// <exception cref="InvalidOperationException">The entity is in no scene.</exception>
    public Scene Scene => SceneOrNull ?? throw new InvalidOperationException(
        $"A {GetType().Name} is in no scene. Reach the scene from OnAddedToScene onward.");

    /// <summary>The scene holding this entity, or null before addition or after removal.</summary>
    public Scene? SceneOrNull { get; internal set; }

    /// <summary>The run of the scene holding this entity, available from <see cref="OnStart"/> on.</summary>
    /// <exception cref="InvalidOperationException">This entity is in no scene or the scene has not started.</exception>
    public Run Run => SceneOrNull is { } scene
        ? scene.Run
        : throw new InvalidOperationException($"{GetType().Name} is in no scene, so {Scenes.Scene.NoRunYet}");

    /// <summary>
    /// The run's default deterministic random stream. A domain whose draws must not move another's
    /// takes its own, as <c>new RandomSource(Random.Seed, MyStreams.Map)</c>.
    /// </summary>
    public RandomSource Random => Run.Random;

    // The scene queued for this entity, between deferred add and drain.
    internal Scene? PendingScene { get; set; }

    // The render layer, read from the root of the chain.
    internal RenderSpace Space => _root.OwnSpace;

    // The offset a renderer adds to reach its draw space.
    internal Vector2 SpaceOrigin => _root.OwnSpaceOrigin;

    // The render layer this entity draws on as a root.
    internal virtual RenderSpace OwnSpace => RenderSpace.World;

    internal virtual Vector2 OwnSpaceOrigin => Vector2.Zero;

    // Set by a subclass holding world coordinates, where position changes are errors.
    internal bool Anchored { get; init; }

    // Whether this entity registers a shape: its own grid or a collider component.
    internal virtual bool Collides => false;

    internal ReadOnlySpan<Component> Components => CollectionsMarshal.AsSpan(_components);

    // The entity's draw band: ancestry ZIndex summed before children read it.
    internal long DrawBand { get; set; }

    // Walks the component list. A hook may detach the current or an earlier component, and the
    // cursor shifts with the list so the shifted-down component still gets visited.
    private ComponentWalk LiveComponents => new(_components);


    /// <summary>Attaches <paramref name="component"/>, which no entity may already own.</summary>
    /// <exception cref="InvalidOperationException">
    /// The component is already attached to an entity, or this entity refuses it. An entity refuses
    /// a second <see cref="Physics.KinematicBody2D"/>, a collider, body or notifier when its
    /// <see cref="ScrollFactor"/> is not one or anything in its ancestry is scaled, and a component
    /// that cannot rotate when anything in its ancestry is rotated.
    /// </exception>
    public void Add(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.Entity is not null)
        {
            throw new InvalidOperationException(
                $"A {component.GetType().Name} is already attached to a {component.Entity.GetType().Name}. Detach it before attaching it elsewhere.");
        }

        TransformSupport supports = component.Supports;
        if ((supports & TransformSupport.Scale) == 0 && ScrollFactor != Vector2.One)
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
        SceneOrNull?.InvalidateRenderers();

        if (SceneOrNull is not null)
        {
            component.EnterScene();
        }

        // A started entity starts each component it gains. Both conditions are re-read because the
        // hooks above may have detached the component or removed this entity from the scene. An
        // entity queued for removal still reports a scene but will not step again, so its new
        // component waits until the next add.
        if (_started && SceneOrNull?.Contains(this) == true && ReferenceEquals(component.Entity, this))
        {
            component.RunStart();
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
                $"This entity does not hold the {component.GetType().Name} being removed.");
        }

        _components.RemoveAt(IndexOf(_components, component));

        // Clear the owner before hooks run. Hooks cannot then observe the component still attached.
        component.Entity = null;
        component.LeaveScene();
        component.OnDetachingFrom(this);
        SceneOrNull?.InvalidateRenderers();
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
    /// Advances this entity by one fixed step, before its components and its <see cref="Children"/>
    /// step. The scene steps in tree order, and a child sees the world position its parent just moved
    /// to. An entity steps only after <see cref="OnStart"/> has run, and so do its components.
    /// </summary>
    protected internal virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Advances the entity after stepping and contact settlement, before the frame is built. Runs
    /// before this entity's components' late step and before the scene's <see cref="Scene.OnLateStep"/>.
    /// The scene calls this only after <see cref="OnStart"/>.
    /// </summary>
    protected internal virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>
    /// Draws this entity's debug geometry. The engine draws the origin cross at
    /// <see cref="WorldPosition"/> before this call, and an override cannot lose it.
    /// </summary>
    protected internal virtual void OnDebugDraw()
    {
    }

    /// <summary>
    /// Fills this entity's panel section. The engine writes <see cref="Name"/>, <see cref="Transform"/>,
    /// <see cref="WorldTransform"/>, <see cref="ZIndex"/>, <see cref="ScrollFactor"/>, <see cref="Visible"/>,
    /// <see cref="Tint"/> and <c>Remove</c> before this call. Components fill their sections after.
    /// </summary>
    protected internal virtual void OnDebugPanel(DebugPanel panel)
    {
    }

    /// <summary>
    /// Runs once before the first step, after the peers added alongside this entity have attached.
    /// A parent starts before its children, and the batch starts together. Components start after.
    /// An entity removed from here never steps.
    /// </summary>
    protected internal virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs when the scene holds this entity, before children join. Peers may not exist yet.
    /// </summary>
    protected internal virtual void OnAddedToScene()
    {
    }

    /// <summary>
    /// Runs when the scene releases this entity, either on removal or when the scene stops.
    /// Descendants leave first, deepest first.
    /// </summary>
    protected internal virtual void OnRemovedFromScene()
    {
    }

    /// <summary>
    /// Appends assets this entity declares beyond those owned by its components. Collection can run
    /// before <see cref="OnStart"/>, so declare from construction-time state. An override appends to
    /// <paramref name="assets"/> and changes nothing else.
    /// </summary>
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

    // Adjusts the movement-collider count on this entity and every ancestor.
    internal void TrackMovement(int delta)
    {
        for (Entity? entity = this; entity is not null; entity = entity._parent)
        {
            entity._movementTrackers += delta;
        }
    }

    // Clears the parent, skipping the checks a public parent write makes.
    internal void Unparent()
    {
        Unlink();
        _parent = null;
        Invalidate(previous: true);
    }

    // Starts the entity once. Rejoining a scene does not restart it or its components.
    internal void RunStart()
    {
        if (!_started)
        {
            _started = true;
            OnStart();
        }

        // Stop if OnStart removed the entity, because a component's start expects a scene to search.
        if (SceneOrNull?.Contains(this) != true)
        {
            return;
        }

        // A component tracks its own started flag, so one attached during OnStart is not started twice.
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

        Scenes.Scene.ThrowCleanupFailures(failures);
    }

    // An entity that has not started does not step, and neither do its components.
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

    // Same rule as RunStep: an entity that has not started takes no late step.
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

    // The engine writes its own rows before any hook runs, and an override cannot lose them. Hooks
    // follow the same started-only rule as RunStep. Every component gets a heading whether or not it
    // has started, so the panel always lists the entity's components.
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
        panel.Toggle("Visible", Visible, value => Visible = value);
        panel.Field("Tint", Tint);
        panel.Command("Remove", () => SceneOrNull?.Remove(this));

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

    // Checks every reason a parent write can fail, before any link is made.
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
                $"A {GetType().Name} is on the screen layer and can only be a root. Parent a plain entity under it to draw on the screen with it.");
        }

        if (_scrollFactor != Vector2.One)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} carrying a scroll factor cannot be placed by a parent. Set the scroll factor on the root instead.");
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

    // Throws if anything in this subtree forbids a scroll factor other than one.
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

    // Removes this entity from its parent's child list. Does not clear _parent.
    private void Unlink()
    {
        if (_parent is { } parent)
        {
            parent._children!.RemoveAt(IndexOf(parent._children, this));
            parent.TrackMovement(-_movementTrackers);
        }
    }

    // Compares by reference, because a subclass may override Equals and two equal instances are still
    // two separate entities here.
    private static int IndexOf<T>(List<T> items, T item)
        where T : class
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private InvalidOperationException Unscrollable(string what) =>
        new($"A {GetType().Name} carries {what}, which answers at the authored position while a scroll factor draws it elsewhere. Set the factor to one, or move {what} out of this subtree.");

    // Draws a cross at the entity's world position on the Origins channel, carrying this step's
    // motion. The driver calls it, and an override cannot lose it.
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

        // Public because foreach only binds to public members. The type itself is private.
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

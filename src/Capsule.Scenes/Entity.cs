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

/// <summary>One object in a scene, in world units with Y-down.</summary>
/// <remarks>
/// <see cref="Position"/> anchors differ by subclass. A subclass carries the behaviour, and shared
/// capabilities attach as <see cref="Component"/>.
/// <para>
/// Under a <see cref="Parent"/>, local <see cref="Position"/>, <see cref="Rotation"/> and
/// <see cref="Scale"/> combine to <see cref="WorldTransform"/>. Rotation and scale reach
/// presentation only. Collision follows position. <see cref="ScreenEntity"/> is the screen-layer
/// counterpart.
/// </para>
/// </remarks>
public partial class Entity
{
    // The half-length of the origin cross's arms, in world units.
    private const float OriginArm = 1.5f;

    private readonly List<Component> _components = [];

    // The first child allocates the list, because most entities have no children.
    private List<Entity>? _children;
    private Entity? _parent;

    // This entity's index in its parent's child list.
    private int _childSlot;

    // The lowest slot an unlink emptied in _children, or -1 when none is empty. The list compacts
    // before it is next read. Unlinking many siblings then costs one pass instead of one shift each.
    private int _firstChildHole = -1;

    // The root of this entity's chain. Render space, space origin, and scroll factor are read from it.
    private Entity _root;

    // Count of colliders in this subtree, used to avoid redundant writes.
    private int _movementTrackers;

    // Count of attached components that step. The step skips the component walk when it is zero.
    private int _steppers;
    private bool _started;
    private Vector2 _scrollFactor = Vector2.One;

    // Facts a subclass fixes at construction, packed into one byte. Every entity carries them.
    private Traits _traits;

    // The pool that owns this entity for life, or null when the entity was never built by one.
    internal IEntityPool? Pool { get; set; }

    // Whether this entity is idle in Pool right now, refused by Scene.Add until it is taken.
    internal bool IdleInPool { get; set; }

    /// <summary>
    /// A marker or pivot at <paramref name="parent"/>'s origin. It has a place in the world and no
    /// behaviour of its own.
    /// </summary>
    /// <remarks>It joins the parent's scene as <see cref="Parent"/> describes.</remarks>
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

    // A plain root at the origin, for an engine child that must hold its components before it is
    // parented. Parenting then checks them against the ancestry before the child is linked.
    internal Entity()
        : this(Vector2.Zero)
    {
    }

    /// <summary>A root entity at <paramref name="position"/>, in world units.</summary>
    /// <param name="position">
    /// The starting position. A spawn does not interpolate from the origin.
    /// </param>
    protected Entity(Vector2 position)
    {
        _root = this;
        Position = position;
        _previousLocal = _local;
        _previousWorld = _local;
    }

    /// <summary>
    /// Places the entity from a document placement or a spawn built in code. The position,
    /// <see cref="Rotation"/>, <see cref="ZIndex"/>, <see cref="ScrollFactor"/> and every
    /// <see cref="AuthorableAttribute"/> value except an entity reference land before the subclass
    /// constructor body runs, and writes in that body override them.
    /// </summary>
    /// <remarks>
    /// <see cref="EntitySpawn.Scale"/> is left to the subclass. An entity with a different anchor adjusts
    /// the position: <c>spawn with { Position = spawn.Position + anchor }</c>.
    /// A class holding a collider or body refuses a turned spawn when it adds that component. To turn
    /// only what can turn, it sets <c>Rotation = 0</c> first and reads <see cref="EntitySpawn.Rotation"/>
    /// itself.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The scroll factor is not one on an entity that refuses one.</exception>
    protected Entity(EntitySpawn spawn)
        : this(spawn.Position)
    {
        Rotation = spawn.Rotation;

        if (spawn.ZIndex is { } band)
        {
            ZIndex = band;
        }

        if (spawn.ScrollFactor is { } factor)
        {
            ScrollFactor = factor;
        }

        spawn.Apply?.Invoke(this, spawn.Members);
    }

    /// <summary>The entity this one is placed by, or null for a root.</summary>
    /// <remarks>
    /// An entity a scene holds keeps its world transform, and its place in the scene, when its parent changes: its
    /// local transform is recomputed, no scene hook runs, and a screen entity's anchors resolve against the new
    /// parent. It takes the new parent's update order and step-mode hold from the next step. Before a scene holds
    /// it, an entity keeps its local transform, which is how a child is built, and enters the scene with its parent.
    /// <see cref="Scene.Remove"/> on this entity alone clears its parent.
    /// </remarks>
    /// <example>
    /// A thrown log leaves its stack where it is:
    /// <code>
    /// log.Parent = null;
    /// </code>
    /// </example>
    /// <exception cref="InvalidOperationException">
    /// The new ancestry conflicts with this subtree, such as a cycle, a scroll factor or a turn a collider refuses;
    /// or, for an entity in or joining a scene, the parent is not in that scene or is being removed, or the entity
    /// cannot keep its world transform under it. The write is also refused during a draw, once this entity is
    /// being removed, or while an ancestor waits to join a scene. The message names the fix.
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

            if (Leaving)
            {
                throw new InvalidOperationException(
                    $"The removal that takes this {GetType().Name} is already landing and would detach it. Reparent it before the step's removals land, or let the removal take it.");
            }

            if ((SceneOrNull ?? PendingScene) is { } scene)
            {
                Reparent(scene, value);
                return;
            }

            for (Entity? above = _parent; above is not null; above = above._parent)
            {
                if (above.PendingScene is not null)
                {
                    throw new InvalidOperationException(
                        $"A {GetType().Name} is under a {above.GetType().Name} queued to join a scene, and a parent write would leave it out. Write the parent before adding the root, or once the tree has joined.");
                }
            }

            if (value is not null)
            {
                RequireParentable(value);
                value.SceneOrNull?.ThrowIfDrawing("parented an entity");
            }

            Link(value);
            Invalidate(previous: true);
            value?.SceneOrNull?.Enqueue(this);
        }
    }

    /// <summary>
    /// The entities this one places, in parenting order. The span is invalid once a child is added or
    /// removed.
    /// </summary>
    public ReadOnlySpan<Entity> Children
    {
        get
        {
            if (_firstChildHole >= 0)
            {
                CompactChildren();
            }

            return _children is null ? default : CollectionsMarshal.AsSpan(_children);
        }
    }

    /// <summary>
    /// A label the debug overlay shows in place of the type name, or null to show the type name. Only
    /// diagnostics read it, and duplicates are allowed.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The render band: an ordering key applied to all attached renderers. Each renderer's draw
    /// order is the sum of this band up the ancestry plus its own
    /// <see cref="Rendering.Renderer.ZIndex"/>.
    /// </summary>
    /// <remarks>
    /// Higher sums draw later. Equal sums use tree order, then attachment order.
    /// </remarks>
    /// <exception cref="InvalidOperationException">It is changed inside a <see cref="Rendering.Renderer.Draw"/>.</exception>
    public int ZIndex
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            SceneOrNull?.ThrowIfDrawing("set an entity's ZIndex");
            field = value;
            SceneOrNull?.InvalidateRenderers();
        }
    }

    /// <summary>
    /// How far this entity's renderers move with the camera, per axis, defaulting to one. One is
    /// world speed, zero is fixed to the screen, below one is further away and above one is nearer.
    /// </summary>
    /// <remarks>
    /// The root's factor applies to its whole subtree. The factor affects drawing only. Position,
    /// colliders and bounds stay in authored space. The camera's <see cref="Camera.ScrollCenter"/>
    /// anchors every layer.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Non-one factors are forbidden on children, screen-layer entities, and subtrees holding a component
    /// that answers at the authored position: a collider, a body, a <see cref="Tiles.TileMapCollider2D"/>
    /// or a <see cref="Rendering.VisibleOnScreenNotifier2D"/>.
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
                        $"A {GetType().Name} has a parent and scrolls with its root. Set the scroll factor on the root.");
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

    /// <summary>Whether a removal requested during this step will take this entity out of its scene when the step ends.</summary>
    /// <remarks>
    /// The entity keeps stepping and colliding until then. A removal of its parent counts. An entity queued to
    /// join this step counts too, and it joins and leaves at the same drain.
    /// </remarks>
    public bool IsRemovalPending
    {
        get
        {
            // A tree shares one scene. An entity queued to join, or under a root queued to join, finds it
            // through the first ancestor that holds or awaits one.
            for (Entity? above = this; above is not null; above = above._parent)
            {
                if ((above.SceneOrNull ?? above.PendingScene) is { } scene)
                {
                    return scene.IsRemovalPending(this);
                }
            }

            return false;
        }
    }

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

    // Set while the scene runs this entity's removal hooks, after it has stopped being held.
    internal bool Leaving
    {
        get => (_traits & Traits.Leaving) != 0;
        set => _traits = value ? _traits | Traits.Leaving : _traits & ~Traits.Leaving;
    }

    // This entity's index in its parent's child list, current once the parent's Children has been read.
    internal int ChildSlot => _childSlot;

    // This entity's index in its scene's step-order list, kept by the scene while it holds the entity.
    internal int SceneSlot { get; set; }

    // Scratch for the scene's root compaction, clear outside it.
    internal bool Marked
    {
        get => (_traits & Traits.Marked) != 0;
        set => _traits = value ? _traits | Traits.Marked : _traits & ~Traits.Marked;
    }

    // The top of this entity's chain, itself for a root.
    internal Entity Root => _root;

    // The render layer, read from the root of the chain.
    internal RenderSpace Space => _root.OwnSpace;

    // The offset a renderer adds to reach its draw space. A world tree has none and makes no call.
    internal Vector2 SpaceOrigin => _root.OnScreen ? ScreenEntity.OriginOf(this, previous: false) : Vector2.Zero;

    // SpaceOrigin as of the previous step, which a renderer interpolates from.
    internal Vector2 PreviousSpaceOrigin => _root.OnScreen ? ScreenEntity.OriginOf(this, previous: true) : Vector2.Zero;

    // The render layer this entity draws on as a root.
    internal RenderSpace OwnSpace => OnScreen ? RenderSpace.Screen : RenderSpace.World;

    // Whether the scene has joined this entity: components entered and OnAddedToScene run. A scene that
    // has not started holds an entity composed, with SceneOrNull set and nothing joined.
    internal bool Joined
    {
        get => (_traits & Traits.Joined) != 0;
        set => _traits = value ? _traits | Traits.Joined : _traits & ~Traits.Joined;
    }

    // Set by ScreenEntity alone.
    internal bool OnScreen
    {
        get => (_traits & Traits.Screen) != 0;
        init => _traits = value ? _traits | Traits.Screen : _traits & ~Traits.Screen;
    }

    // Set by a subclass holding world coordinates, where position changes are errors.
    internal bool Anchored
    {
        get => (_traits & Traits.Anchored) != 0;
        init => _traits = value ? _traits | Traits.Anchored : _traits & ~Traits.Anchored;
    }

    internal ReadOnlySpan<Component> Components => CollectionsMarshal.AsSpan(_components);

    // The entity's draw band: ancestry ZIndex summed before children read it.
    internal long DrawBand { get; set; }

    // Walks the component list. A hook may detach the current or an earlier component, and the
    // cursor shifts with the list so the shifted-down component still gets visited.
    private ComponentWalk LiveComponents => new(_components);

    /// <summary>Attaches <paramref name="component"/>, which no entity may already own.</summary>
    /// <exception cref="InvalidOperationException">
    /// The component is already attached to an entity, or this entity refuses it. An entity refuses
    /// a second <see cref="Physics.KinematicBody2D"/>, a collider, body, tile-map collider or notifier
    /// when its <see cref="ScrollFactor"/> is not one or anything in its ancestry is scaled, and a
    /// component that cannot rotate when anything in its ancestry is rotated. Only a
    /// <see cref="Tiles.TileMap"/> whose palette names a layer takes one
    /// <see cref="Tiles.TileMapCollider2D"/>. A refused component stays unattached, as does one whose own
    /// <see cref="Component.OnAttached"/> throws. It is also thrown inside a
    /// <see cref="Rendering.Renderer.Draw"/>.
    /// </exception>
    /// <remarks>
    /// The component takes the next place in attachment order before its <see cref="Component.OnAttached"/>
    /// runs. A component that attaches its parts there steps before them.
    /// </remarks>
    public void Add(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        SceneOrNull?.ThrowIfDrawing("attached a component");

        if (component.Entity is not null)
        {
            throw new InvalidOperationException(
                $"A {component.GetType().Name} is already attached to a {component.Entity.GetType().Name}. Detach it before attaching it elsewhere.");
        }

        TransformSupport supports = component.Supports;
        if ((supports & TransformSupport.Scroll) == 0 && ScrollFactor != Vector2.One)
        {
            throw Unscrollable(component);
        }

        for (Entity? above = this; above is not null; above = above._parent)
        {
            if ((supports & TransformSupport.Rotation) == 0 && above._local.Rotation != 0f)
            {
                throw Turned(component, this, above, above._local.Rotation);
            }

            if ((supports & TransformSupport.Resize) == 0 && above._local.Scale != Vector2.One)
            {
                throw Scaled(component, this, above, above._local.Scale);
            }
        }

        component.Entity = this;
        _components.Add(component);
        if (component.Steps)
        {
            _steppers++;
        }

        bool registered = false;
        try
        {
            component.OnAttachedTo(this);
            registered = true;
            component.OnAttached(this);
        }
        catch
        {
            // The attach leaves no trace. A component that detached itself before throwing is already
            // gone, and one refusing its entity in OnAttachedTo registered nothing.
            if (ReferenceEquals(component.Entity, this))
            {
                if (registered)
                {
                    component.OnDetachingFrom(this);
                }

                _components.RemoveAt(IndexOf(_components, component));
                if (component.Steps)
                {
                    _steppers--;
                }

                component.Entity = null;
            }

            // OnAttached may have attached and detached other components before throwing.
            SceneOrNull?.InvalidateRenderers();
            throw;
        }

        // Attaching to an entity a scene already holds changes that scene's renderer set.
        SceneOrNull?.InvalidateRenderers();

        // OnAttached may have detached the component, which must then not enter the scene. An entity
        // composed by a scene that has not started enters its components when it joins.
        if (Joined && ReferenceEquals(component.Entity, this))
        {
            component.EnterScene();
        }

        // The hooks above may have detached the component or removed this entity. An entity queued to
        // leave, by itself or with an ancestor, still steps until the drain. Its new component waits for
        // the next add before it starts. A scene that has not started starts it as the entity joins.
        if (_started && Joined && SceneOrNull?.Contains(this) == true && ReferenceEquals(component.Entity, this))
        {
            component.RunStart();
        }
    }

    /// <summary>Detaches <paramref name="component"/> so it may be attached elsewhere.</summary>
    /// <exception cref="InvalidOperationException">
    /// The component is not attached to this entity, or it is called inside a <see cref="Rendering.Renderer.Draw"/>.
    /// </exception>
    public void Remove(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        SceneOrNull?.ThrowIfDrawing("detached a component");

        if (!ReferenceEquals(component.Entity, this))
        {
            throw new InvalidOperationException(
                $"This entity does not hold the {component.GetType().Name} being removed.");
        }

        _components.RemoveAt(IndexOf(_components, component));
        if (component.Steps)
        {
            _steppers--;
        }

        // Clear the owner before hooks run. Hooks cannot then observe the component still attached.
        component.Entity = null;

        // Every hook runs and the engine's own release always follows, so a throwing hook leaves no
        // detached component registered or drawn. Failures propagate once detachment finishes.
        List<Exception>? failures = null;
        try
        {
            component.LeaveScene();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            component.OnDetached(this);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        component.OnDetachingFrom(this);
        SceneOrNull?.InvalidateRenderers();
        Scenes.Scene.ThrowCleanupFailures(failures);
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
    /// step. The scene steps in tree order, and a child sees the world position its parent just
    /// moved to.
    /// </summary>
    /// <remarks>
    /// An entity and its components step only after <see cref="OnStart"/> has run, and not while
    /// <see cref="StepMode"/> holds them.
    /// </remarks>
    protected internal virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Advances the entity after stepping and contact settlement, before the frame is built. Runs
    /// before this entity's components' late step and before the scene's
    /// <see cref="Scene.OnLateStep"/>.
    /// </summary>
    /// <remarks>The scene calls this only after <see cref="OnStart"/>.</remarks>
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
    /// Fills this entity's panel section. The engine writes <see cref="Name"/>,
    /// <see cref="Transform"/>, <see cref="WorldTransform"/>, <see cref="ZIndex"/>,
    /// <see cref="ScrollFactor"/>, <see cref="Visible"/>, <see cref="Tint"/>, <see cref="Flash"/>,
    /// <see cref="StepMode"/> and <c>Remove</c> before this call.
    /// </summary>
    /// <remarks>Components fill their sections after.</remarks>
    protected internal virtual void OnDebugPanel(DebugPanel panel)
    {
    }

    /// <summary>
    /// Runs once before the first step, after the peers added alongside this entity have attached.
    /// A parent starts before its children, and the batch starts together.
    /// </summary>
    /// <remarks>
    /// Components start after. An entity removed during this call never steps, and its components
    /// do not start.
    /// <para>
    /// An entity in the scene when it starts runs this before the scene's first frame is built. One
    /// added later runs it before the first frame that draws it: at once outside a step, or as the step
    /// that added it ends.
    /// </para>
    /// </remarks>
    protected internal virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs when the scene joins this entity, before children join. Peers may not exist yet.
    /// </summary>
    /// <remarks>
    /// A scene that has not started only composes what it is given. It joins everything composed as it
    /// starts, after its assets are preloaded and before any <see cref="OnStart"/>. <see cref="Run"/> is
    /// reachable here.
    /// </remarks>
    protected internal virtual void OnAddedToScene()
    {
    }

    /// <summary>
    /// Runs when the scene releases this entity, either on removal or when the scene stops.
    /// Descendants leave first, deepest first.
    /// </summary>
    /// <remarks>
    /// An entity that never joined runs none. That covers one removed before its scene started and one
    /// held by a scene released without starting.
    /// </remarks>
    protected internal virtual void OnRemovedFromScene()
    {
    }

    /// <summary>
    /// Appends assets this entity declares beyond those owned by its components. Collection can run
    /// before <see cref="OnStart"/>.
    /// </summary>
    /// <remarks>
    /// Declare from construction-time state. An override appends to <paramref name="assets"/> and
    /// changes nothing else.
    /// </remarks>
    protected internal virtual void CollectAssets(AssetCollection assets)
    {
    }

    internal void CollectAssetPreloads(AssetCollection assets)
    {
        Scene? scene = assets.GatheringScene as Scene;
        if (scene is not null && !scene.FirstCollected(this))
        {
            return;
        }

        IEntityPool? previous = scene?.EnterDeclaringPool(OwningSharedPool());
        try
        {
            CollectAssets(assets);

            foreach (Component component in Components)
            {
                component.CollectAssets(assets);

                if (component is Renderer { Material: { } material })
                {
                    assets.Add(material);
                }
            }
        }
        finally
        {
            scene?.RestoreDeclaringPool(previous);
        }
    }

    // The scene's shared pool holding this entity or its nearest ancestor a shared pool holds, or null.
    private IEntityPool? OwningSharedPool()
    {
        for (Entity? entity = this; entity is not null; entity = entity._parent)
        {
            if (entity.Pool is { Shared: true } pool)
            {
                return pool;
            }
        }

        return null;
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

    // Follows a component whose Steps changed while attached to this entity.
    internal void CountStepper(bool steps) => _steppers += steps ? 1 : -1;

    // An entity that has not started or is held does not step, and neither do its components.
    internal void RunStep(in StepContext context)
    {
        if (!_started || Held)
        {
            return;
        }

        OnStep(context);

        if (_steppers == 0)
        {
            return;
        }

        foreach (Component component in LiveComponents)
        {
            component.RunStep(context);
        }
    }

    // Same rule as RunStep: an entity that has not started or is held takes no late step.
    internal void RunLateStep(in StepContext context)
    {
        if (!_started || Held)
        {
            return;
        }

        OnLateStep(context);

        if (_steppers == 0)
        {
            return;
        }

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
        panel.Field("Flash", Flash);
        panel.Field("StepMode", StepMode);
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

    private void Link(Entity? parent)
    {
        Unlink();
        _parent = parent;

        if (parent is not null)
        {
            // Children that come and go unread would otherwise grow the parent's list by a slot each time.
            if (parent._firstChildHole >= 0)
            {
                parent.CompactChildren();
            }

            List<Entity> siblings = parent._children ??= [];
            _childSlot = siblings.Count;
            siblings.Add(this);
            parent.TrackMovement(_movementTrackers);

            if (OnScreen)
            {
                ScreenEntity.Reflow(parent);
            }
        }
    }

    // Every refusal comes before the first link.
    private void Reparent(Scene scene, Entity? parent)
    {
        scene.ThrowIfDrawing("reparented an entity");

        // A removal asked of an ancestor refuses only once it lands. Until then the write takes this entity out of it.
        if (scene.IsRemovalRequested(this))
        {
            throw new InvalidOperationException(
                $"Scene.Remove was called on this {GetType().Name}, so it cannot be reparented. Reparent it before requesting its removal, or let the removal take it.");
        }

        if (scene.IsDetaching(this))
        {
            throw new InvalidOperationException(
                $"The removal that takes this {GetType().Name} is already landing and would detach it. Reparent it before the step's removals land, or let the removal take it.");
        }

        if (parent is not null)
        {
            if (!ReferenceEquals(parent.SceneOrNull, scene))
            {
                throw new InvalidOperationException(parent.SceneOrNull is null
                    ? $"A {GetType().Name} is in a scene and cannot be placed by a {parent.GetType().Name} that is in none. Parent it once the {parent.GetType().Name} has joined this scene, or set the parent to null."
                    : $"A {GetType().Name} is in one scene and cannot be placed by a {parent.GetType().Name} in another. Parent it under an entity of its own scene, or set the parent to null.");
            }

            if (scene.IsRemovalPending(parent) || scene.IsDetaching(parent))
            {
                throw new InvalidOperationException(
                    $"A {parent.GetType().Name} is being removed and would take a {GetType().Name} placed by it along. Parent it under an entity that stays, or set the parent to null.");
            }

            RequireParentable(parent);
        }

        Transform2D local = _local;
        Transform2D previousLocal = _previousLocal;
        bool held = SceneOrNull is not null;
        if (held)
        {
            local = Beneath(parent, previous: false, World);
            previousLocal = Beneath(parent, previous: true, _previousWorld);
            if (!IsFinite(local) || !IsFinite(previousLocal))
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} cannot keep its world transform under that parent: a world scale has a zero axis, or a value is too large. Give the parent a scale on both axes, reduce the scales or positions in its ancestry, or set the parent to null.");
            }
        }

        bool wasRoot = _parent is null;
        Link(parent);
        _local = local;
        _previousLocal = previousLocal;
        Invalidate(previous: true);

        if (held)
        {
            scene.Reparented(this, wasRoot);
        }
    }

    // The local transform that composes under `parent`, or under none, into `world`.
    private static Transform2D Beneath(Entity? parent, bool previous, in Transform2D world) =>
        parent is null ? world : Within(previous ? parent._previousWorld : parent.World, world);

    private static bool IsFinite(in Transform2D transform) =>
        float.IsFinite(transform.Position.X) && float.IsFinite(transform.Position.Y)
        && float.IsFinite(transform.Rotation) && float.IsFinite(transform.Scale.X) && float.IsFinite(transform.Scale.Y);

    // Checks every reason a parent write can fail, before any link is made.
    private void RequireParentable(Entity parent)
    {
        if (Anchored)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} is anchored at the world origin and cannot be placed by a parent.");
        }

        if (OwnSpace == RenderSpace.Screen && parent is not ScreenEntity)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} is on the screen layer and cannot be placed by the plain {parent.GetType().Name}. Make the parent a ScreenEntity, or parent a plain entity under this one instead.");
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
        if (FirstRefuser(TransformSupport.Scroll) is var (component, _))
        {
            throw Unscrollable(component);
        }
    }

    // Removes this entity from its parent's child list. Does not clear _parent.
    private void Unlink()
    {
        if (_parent is { } parent)
        {
            parent._children![_childSlot] = null!;
            parent._firstChildHole = parent._firstChildHole < 0 ? _childSlot : Math.Min(parent._firstChildHole, _childSlot);
            parent.TrackMovement(-_movementTrackers);

            if (OnScreen)
            {
                ScreenEntity.Reflow(parent);
            }
        }
    }

    // Closes the holes unlinks left, keeping parenting order, and renumbers what moved.
    private void CompactChildren()
    {
        Span<Entity> children = CollectionsMarshal.AsSpan(_children);
        int kept = _firstChildHole;
        for (int index = kept + 1; index < children.Length; index++)
        {
            Entity child = children[index];
            if (child is not null)
            {
                child._childSlot = kept;
                children[kept++] = child;
            }
        }

        _children!.RemoveRange(kept, _children.Count - kept);
        _firstChildHole = -1;
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

    private InvalidOperationException Unscrollable(Component component) =>
        new($"A {component.GetType().Name} answers at its authored position while a scroll factor draws this {GetType().Name} elsewhere. Remove the {component.GetType().Name}, or set the scroll factor to one.");

    // Draws a cross at the entity's world position on the Origins channel, carrying this step's
    // motion. The driver calls it, and an override cannot lose it.
    private void DrawOrigin()
    {
        Vector2 position = World.Position;
        Vector2 motion = position - _previousWorld.Position;
        DebugDraw.Line(DebugDraw.Origins, position - new Vector2(OriginArm, 0f), position + new Vector2(OriginArm, 0f), null, motion);
        DebugDraw.Line(DebugDraw.Origins, position - new Vector2(0f, OriginArm), position + new Vector2(0f, OriginArm), null, motion);
    }

    [Flags]
    private enum Traits : byte
    {
        None = 0,
        Anchored = 1,
        Screen = 2,
        Leaving = 4,
        Marked = 8,
        Joined = 16,
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

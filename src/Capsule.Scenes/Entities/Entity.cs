using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.UI;

namespace Capsule.Scenes;

/// <summary>One object in a scene, in world units with Y-down.</summary>
/// <remarks>
/// What <see cref="Position"/> anchors differs by subclass.
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

    // Each entity type's name, read once and held. The runtime's own cache of a type's name can be
    // collected and rebuilt on the next read, which allocates, and a join reads the name.
    private static readonly ConcurrentDictionary<Type, string> TypeNames = new();

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

    // Whether a renderer here may be at rest. Whatever moves the entity or changes how it shows wakes them.
    private bool _holdsRest;
    private Vector2 _scrollFactor = Vector2.One;

    // The name the game or its scene document asked for, null for the type name. Name spells a numbered one
    // on each read, and a find by name compares this one without allocating.
    private string? _requestedName;

    // Facts a subclass fixes at construction, packed into one byte. Every entity carries them.
    private Traits _traits;

    // The pool that owns this entity for life, or null when the entity was never built by one.
    internal IEntityPool? Pool { get; set; }

    // Whether this entity is idle in Pool right now, refused by Scene.Add until it is taken.
    internal bool IdleInPool { get; set; }

    // The number the scene gave the name as the entity joined or was renamed, 0 for none.
    internal int NameNumber { get; set; }

    /// <summary>
    /// The entity's name in its scene: the name set here or by its scene document entry, or the type name
    /// while none is set.
    /// </summary>
    /// <remarks>
    /// The debug overlay, a trace and the game's logs show the name, and
    /// <see cref="Scene.FindFirst{T}(string)"/> finds an entity by it without its number.
    /// <para>
    /// Joining a scene numbers the name when the scene has held it before: the first <c>Avatar</c> stays
    /// <c>Avatar</c>, and later ones are <c>Avatar (1)</c>, <c>Avatar (2)</c> and on. A number is never
    /// reused in that scene, and an entity keeps its name while others come and go. A
    /// pooled entity taken again joins as a new one with a new number. Setting another name while in a scene
    /// numbers it the same way at once, and setting the name it already asks for changes nothing. A number
    /// in parentheses ending a name is the scene's to give, and setting a name that ends in one throws.
    /// <c>Scene</c> and <c>Camera</c> name the scene's own panel and its camera's in the debug overlay and a
    /// trace, and an entity asking for either is numbered from <c>(1)</c>.
    /// </para>
    /// </remarks>
    [AllowNull]
    public string Name
    {
        get => NameNumber == 0
            ? RequestedName
            : string.Create(CultureInfo.InvariantCulture, $"{RequestedName} ({NameNumber})");

        set
        {
            if (value is not null && EndsInNumber(value))
            {
                throw new ArgumentException(
                    $"'{value}' ends in a number in parentheses, which the scene gives to a name it has held before. Set the name without it.",
                    nameof(value));
            }

            string previous = RequestedName;
            _requestedName = value;
            if (!Joined || SceneOrNull is not { } scene)
            {
                NameNumber = 0;
            }
            else if (!string.Equals(RequestedName, previous, StringComparison.Ordinal))
            {
                NameNumber = scene.NextNameNumber(RequestedName);
            }
        }
    }

    /// <summary>
    /// The render band: an ordering key applied to all attached renderers. Each renderer's draw
    /// order is the sum of this band up the ancestry plus its own
    /// <see cref="Rendering.Renderer.ZIndex"/>.
    /// </summary>
    /// <remarks>
    /// Higher sums draw later. Equal sums use tree order, then attachment order. The sum stops at an
    /// entity whose <see cref="ZIndexIsAbsolute"/> is set.
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

            SceneOrNull?.Drawing.ThrowIfDrawing("set an entity's ZIndex");
            SceneOrNull?.Drawing.Changing(this, band: true);
            field = value;
        }
    }

    /// <summary>
    /// Whether <see cref="ZIndex"/> is this entity's whole band, with no ancestor's added to it.
    /// </summary>
    /// <remarks>
    /// A child that sets it keeps one band while its parent changes its own. Its descendants sum from
    /// it, and each renderer's <see cref="Rendering.Renderer.ZIndex"/> still adds. The band is absolute
    /// within the entity's layer, which is its root's. A root's band is its own already, and the flag
    /// changes nothing until the entity takes a parent. Among equal bands it keeps its place in tree order,
    /// under its parent.
    /// </remarks>
    /// <example>
    /// <code>
    /// Entity glow = new(lantern) { ZIndex = 40, ZIndexIsAbsolute = true };
    /// </code>
    /// </example>
    /// <exception cref="InvalidOperationException">It is changed inside a <see cref="Rendering.Renderer.Draw"/>.</exception>
    public bool ZIndexIsAbsolute
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            SceneOrNull?.Drawing.ThrowIfDrawing("set an entity's ZIndexIsAbsolute");
            SceneOrNull?.Drawing.Changing(this, band: true);
            field = value;
        }
    }

    /// <summary>The scene holding this entity, or null before addition or after removal.</summary>
    public Scene? SceneOrNull { get; internal set; }

    // The scene queued for this entity, between deferred add and drain.
    internal Scene? PendingScene { get; set; }

    // This entity's index in its scene's step-order list, kept by the scene while it holds the entity.
    internal int SceneSlot { get; set; }

    // The entity's draw band: ZIndex summed up the ancestry as far as an absolute one, before children read it.
    internal long DrawBand { get; private set; }

    // Sums the band from the parent's, which the order's walk has already summed.
    internal long SumDrawBand() => DrawBand = (ZIndexIsAbsolute ? 0 : Parent?.DrawBand ?? 0) + ZIndex;

    // Sums the band up the ancestry, trusting no ancestor's sum.
    internal long SumDrawBandFromAncestors()
    {
        long band = 0;
        for (Entity? above = this; above is not null; above = above.ZIndexIsAbsolute ? null : above.Parent)
        {
            band += above.ZIndex;
        }

        return DrawBand = band;
    }

    // This entity's place in its scene's tree order: higher than every entity before it and lower than
    // every one after. It stands until the scene spreads its labels again.
    internal long Order { get; set; }

    // The entities before and after this one in its scene's tree order.
    internal Entity? OrderPrev { get; set; }

    internal Entity? OrderNext { get; set; }

    // Whether the entity joined its scene since the draw order was last read, and holds no key in it yet.
    internal bool AwaitsDrawOrder { get; set; }

    // Whether a parent write moved the entity along its scene's tree order and its slot in the list has
    // not followed yet. The scene keeps one entry for it while this is set.
    internal bool AwaitsSlot
    {
        get => (_traits & Traits.AwaitsSlot) != 0;
        set => _traits = value ? _traits | Traits.AwaitsSlot : _traits & ~Traits.AwaitsSlot;
    }

    // Whether a parent write moved the entity's subtree since its scene's last step began, and the next
    // resolves the subtree's holds again. The scene keeps one entry for it while this is set.
    internal bool AwaitsHold { get; set; }

    /// <summary>
    /// A marker or pivot at <paramref name="parent"/>'s origin. It has a place in the world and no
    /// behaviour of its own.
    /// </summary>
    /// <remarks>It joins the parent's scene as <see cref="Parent"/> describes.</remarks>
    public Entity(Entity parent)
        : this(parent, Vector2.Zero)
    {
    }

    /// <summary>A marker or pivot under <paramref name="parent"/> at <paramref name="position"/> in parent space.</summary>
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
    /// <see cref="Rotation"/>, <see cref="Name"/>, <see cref="ZIndex"/>, <see cref="ScrollFactor"/>, <see cref="Tint"/>,
    /// <see cref="Unlit"/> and every <see cref="AuthorableAttribute"/> value except an entity reference land before the subclass
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

        if (spawn.Name is { } name)
        {
            Name = name;
        }

        if (spawn.ZIndex is { } band)
        {
            ZIndex = band;
        }

        if (spawn.ScrollFactor is { } factor)
        {
            ScrollFactor = factor;
        }

        if (spawn.Tint is { } tint)
        {
            Tint = tint;
        }

        if (spawn.Unlit is { } unlit)
        {
            Unlit = unlit;
        }

        spawn.Apply?.Invoke(this, spawn.Members);
    }

    /// <summary>The entity this one is placed by, or null for a root.</summary>
    /// <remarks>
    /// An entity a scene holds keeps its world transform and its place in the scene, and no scene hook runs. It
    /// takes the new parent's step order and step-mode hold from the next step. An entity no scene holds keeps its
    /// local transform and joins the scene with its parent. <see cref="Scene.Remove"/> on this entity alone clears
    /// its parent.
    /// </remarks>
    /// <example>
    /// A thrown log leaves its stack where it is:
    /// <code>
    /// log.Parent = null;
    /// </code>
    /// </example>
    /// <exception cref="InvalidOperationException">
    /// The new ancestry makes a cycle, or holds a scroll factor, turn or scale a component in this subtree refuses.
    /// For an entity in or joining a scene, the parent is outside that scene or being removed, or the entity cannot
    /// keep its world transform under it. The write is also refused during a draw, once this entity is being
    /// removed, while an ancestor waits to join a scene, and under a parent a scene holds while this entity is
    /// idle in its pool.
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
                value.SceneOrNull?.Drawing.ThrowIfDrawing("parented an entity");
                value.SceneOrNull?.RequireEnqueueable(this);
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

            if (value != _scrollFactor)
            {
                WakeTree();
            }

            _scrollFactor = value;
        }
    }

    /// <summary>The scene holding this entity.</summary>
    /// <exception cref="InvalidOperationException">The entity is in no scene.</exception>
    public Scene Scene => SceneOrNull ?? throw new InvalidOperationException(
        $"A {GetType().Name} is in no scene. Reach the scene from OnAddedToScene onward.");

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
        : throw new InvalidOperationException($"{GetType().Name} is in no scene and has no run yet. {Scenes.Scene.NoRunYet}");

    /// <summary>The run's deterministic random source, <see cref="Capsule.Run.Random"/>.</summary>
    public RandomSource Random => Run.Random;

    // Set while the scene runs this entity's removal hooks, after it has stopped being held.
    internal bool Leaving
    {
        get => (_traits & Traits.Leaving) != 0;
        set => _traits = value ? _traits | Traits.Leaving : _traits & ~Traits.Leaving;
    }

    // This entity's index in its parent's child list, current once the parent's Children has been read.
    internal int ChildSlot => _childSlot;

    // Whether the scene holding this entity keeps an entry for it among the entities waiting to start.
    internal bool WaitsToStart
    {
        get => (_traits & Traits.WaitsToStart) != 0;
        set => _traits = value ? _traits | Traits.WaitsToStart : _traits & ~Traits.WaitsToStart;
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

    // Whether this entity's screen intent draws in the world's order, read from the root of the chain.
    internal bool InWorldOrder => _root.SortedWithWorld;

    // Set by ScreenEntity.SortsWithWorld, on a root alone.
    internal bool SortedWithWorld
    {
        get => (_traits & Traits.WorldOrder) != 0;
        set => _traits = value ? _traits | Traits.WorldOrder : _traits & ~Traits.WorldOrder;
    }

    // Whether the scene has joined this entity: components entered and OnAddedToScene run. A scene that
    // has not started holds an entity composed, with SceneOrNull set and nothing joined.
    internal bool Joined
    {
        get => (_traits & Traits.Joined) != 0;
        set => _traits = value ? _traits | Traits.Joined : _traits & ~Traits.Joined;
    }

    // The name the scene numbers, before its number.
    internal string RequestedName => _requestedName ?? TypeNames.GetOrAdd(GetType(), static type => type.Name);


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

    // A renderer here went to rest. Its world transform and its appearance are both fresh, and whatever
    // stales either reaches this entity.
    internal void HoldRest() => _holdsRest = true;

    // Whether the entity stands where the previous step left it.
    internal bool Still
    {
        get
        {
            // Compared member by member, the place first, which is what a moving entity differs in.
            ref readonly Transform2D world = ref World;

            return world.Position == _previousWorld.Position && world.Rotation == _previousWorld.Rotation && world.Scale == _previousWorld.Scale;
        }
    }

    internal void WakeRenderers()
    {
        if (!_holdsRest)
        {
            return;
        }

        _holdsRest = false;
        foreach (Component component in _components)
        {
            (component as Renderer)?.Wake();
        }
    }

    private void WakeTree()
    {
        if (_holdsRest)
        {
            WakeRenderers();
        }

        foreach (Entity child in Children)
        {
            child.WakeTree();
        }
    }

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
    /// Fills this entity's panel section. The engine writes <see cref="Parent"/>,
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
    /// Appends assets this entity declares beyond those its components own and its class's code names.
    /// Collection can run before <see cref="OnStart"/>.
    /// </summary>
    /// <inheritdoc cref="Component.CollectAssets" path="/remarks"/>
    protected internal virtual void CollectAssets(AssetCollection assets)
    {
    }

    internal void CollectAssetPreloads(AssetCollection assets)
    {
        Scene? scene = assets.GatheringScene as Scene;
        if (scene is not null && !scene.Pools.FirstCollected(this))
        {
            return;
        }

        scene?.ReserveName(RequestedName);

        IEntityPool? previous = scene?.Pools.EnterDeclaringPool(OwningSharedPool());
        try
        {
            assets.AddClassAssets(this);
            CollectAssets(assets);

            if (Material is { } own)
            {
                assets.Add(own);
            }

            foreach (Component component in Components)
            {
                assets.AddClassAssets(component);
                component.CollectAssets(assets);

                if (component is Renderer { Material: { } material })
                {
                    assets.Add(material);
                }
            }
        }
        finally
        {
            scene?.Pools.RestoreDeclaringPool(previous);
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

        // A component tracks its own started flag. One attached during OnStart is not started twice.
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

    // Whether name ends as the scene numbers one: a space, then digits in parentheses.
    internal static bool EndsInNumber(string name)
    {
        ReadOnlySpan<char> text = name;
        int open = text.LastIndexOf(" (", StringComparison.Ordinal);

        return open >= 0
            && text.Length > open + 3
            && text[^1] == ')'
            && !text[(open + 2)..^1].ContainsAnyExceptInRange('0', '9');
    }

    // The engine writes its own rows before any hook runs, and an override cannot lose them. Hooks
    // follow the same started-only rule as RunStep. Every component gets a heading whether or not it
    // has started, and the panel always lists the entity's components.
    internal void RunDebugPanel(DebugPanel panel)
    {
        panel.Section("Entity");
        if (_parent is not null)
        {
            panel.Field("Parent", _parent);
        }

        panel.Field("Transform", _local);
        if (_parent is not null)
        {
            panel.Field("World Transform", World);
        }

        panel.Field("ZIndex", ZIndex);
        if (_parent is not null)
        {
            panel.Toggle("ZIndexIsAbsolute", ZIndexIsAbsolute, value => ZIndexIsAbsolute = value);
        }

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
        AwaitsSlot = 8,
        Joined = 16,
        ClipFresh = 32,
        WorldOrder = 64,
        WaitsToStart = 128,
    }
}

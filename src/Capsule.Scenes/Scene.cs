using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tiles;

namespace Capsule.Scenes;

/// <summary>
/// An ordered world of entities with a <see cref="Camera"/> that frames it and a
/// <see cref="Collision"/> world it collides through. Mutations requested during a step are
/// deferred until the step ends.
/// </summary>
/// <remarks>
/// Populate the scene with <see cref="Add"/>, search it with <see cref="FindSingle{T}"/>, and override
/// <see cref="OnStart"/>, <see cref="OnStep"/>, <see cref="OnLateStep"/> and
/// <see cref="CollectAssets"/> to give it behaviour and assets.
/// </remarks>
public class Scene
{
    internal const string NoRunYet =
        "it has no run yet. Reach the run from OnStart onward, not from a constructor.";

    private readonly EntityTree _tree;
    private readonly List<Entity> _pendingAdds = [];
    private readonly List<Entity> _pendingRemoves = [];

    // Membership checked by reference identity, not by Equals.
    private readonly HashSet<Entity> _pendingAddSet = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Entity> _pendingRemoveSet = new(ReferenceEqualityComparer.Instance);

    // Attached but not started. Entities start as a batch, so each can search the scene as it starts.
    private readonly List<Entity> _pendingStarts = [];

    private readonly SceneRenderIndex _renderIndex = new();
    private readonly SettleList<Collider2D> _contactReporters = new();

    // How many bodies in this scene are moved by each collision layer, indexed by layer.
    private readonly int[] _movedByCounts = new int[CollisionWorld2D.MaxLayers];

    private readonly SettleList<VisibleOnScreenNotifier2D> _screenNotifiers = new();

    // Notifiers that joined between steps, settled as the next step begins.
    private readonly List<VisibleOnScreenNotifier2D> _arrivals = [];

    // The frame's visible region, held so notifiers settle against it.
    private Rect _settledRegion;

    private Camera _camera = new();
    private Run? _run;

    private bool _stepping;

    // Set once the step's entity walks are over. A parent write after that rebuilds step order at once.
    private bool _drained;

    // The roots of the detaches landing now, nested when a removal hook starts another.
    private readonly List<Entity> _detachingRoots = [];

    private int _attaching;
    private bool _starting;
    private bool _started;

    private bool _stopped;
    private bool _drawing;
    private TextureSampling? _sampling;

    // Steps a freeze still holds after the current one. BeginStep settles it with Paused into this
    // step's state, which every entity resolves against. The tree resolves again only when that state
    // or a StepMode has changed.
    private int _freezeTicks;
    private bool _pausedThisStep;
    private bool _frozenThisStep;
    private bool _holdsStale;

    // How many steps have begun, wrapping. A held particle emitter compares it for equality to tell its
    // particles spawned this step from older ones.
    internal int StepsBegun { get; private set; }

    // The current stepping tick, or null if not stepping.
    internal long? SteppingTick { get; private set; }

    // One bit per layer some body in this scene is moved by. A collider on none of them shoves nothing.
    internal ulong MovedByLayers { get; private set; }

    // The deepest any body in this scene has stood below the pose it sweeps. A pusher looks this far
    // below its path for the bodies whose swept pose it crosses.
    internal float DeepestSink { get; private set; }

    // Every texture and sound the document's placements author, which the preload collects. Null for a scene built in code.
    private readonly AssetCollection? _authoredAssets;

    // Whether a preload collection has run. Start runs one first when no host did.
    private bool _preloadsCollected;

    // The shared entity pools by entity type, and in the order built. Collecting a pool can declare
    // another, which appends to the list the collection walks.
    private readonly Dictionary<Type, IEntityPool> _pools = [];
    private readonly List<IEntityPool> _poolOrder = [];

    // The running collection's state, cleared when it ends. Each collection sums declarations afresh. A
    // pool never shrinks, and holds the most any one collection declared.
    private readonly Dictionary<Type, PoolDeclarations> _declared = [];

    // Every entity the running collection has reached. An entity reached twice, live in the scene and
    // held by the pool that forwards it, declares once.
    private readonly HashSet<Entity> _collected = [];

    // For each shared pool, the pools whose entities declared it in the running collection.
    private readonly Dictionary<IEntityPool, HashSet<IEntityPool>> _grownBy = [];

    // The shared pool whose entity is declaring now, or null when the scene or an entity outside every
    // shared pool declares. Something a pooled entity forwards declares as that entity.
    private IEntityPool? _declaringPool;

    // Handed out one at a time to each particle emitter added, so every emitter in a scene draws its
    // own randomness stream. A new scene instance starts at 0.
    private ulong _nextParticleStream;

    /// <summary>An empty world, for a scene that builds itself in code.</summary>
    public Scene()
    {
        _tree = new(this);
    }

    /// <summary>
    /// The world a scene document describes: one entity per entry, in authored order. The document is construction data and is not retained.
    /// </summary>
    /// <exception cref="SpawnException">
    /// A placement's type key is claimed by no entity, or its class returned no entity.
    /// </exception>
    /// <exception cref="SceneDocumentFormatException">
    /// A placement's or the document's members do not match its class's authorable members, or an entity's
    /// constructor refuses what its placement authors with an <see cref="ArgumentException"/> or an
    /// <see cref="InvalidOperationException"/>.
    /// </exception>
    public Scene(SceneContent content)
        : this()
    {
        ArgumentNullException.ThrowIfNull(content.Document);
        ArgumentNullException.ThrowIfNull(content.Entities);

        // References are set once every entry is constructed. A reference may name a later entry.
        ReadOnlySpan<SceneDocumentEntry> entries = content.Document.Entries;
        Dictionary<int, Entity> placed = [];
        AuthoredMembers[] spawned = new AuthoredMembers[entries.Length];
        _authoredAssets = new AssetCollection();
        for (int i = 0; i < entries.Length; i++)
        {
            SceneDocumentEntry entry = entries[i];
            AuthoredMembers authored = new(entry, i, placed, _authoredAssets);
            Entity entity;
            try
            {
                entity = content.Entities.Create(entry.Spawn with { Type = entry.Type }, authored);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw authored.Refused(ex);
            }

            Add(entity);
            spawned[i] = authored;
            if (entry.Id is { } id)
            {
                placed.Add(id, entity);
            }

            // The size the scene authors, applied below, overrides the one its maps span.
            if (entity is TileMap map)
            {
                Size = Vector2.Max(Size, map.Size);
            }
        }

        foreach (AuthoredMembers authored in spawned)
        {
            authored.Finish();
        }

        // The scene's own members land once every entry is built, before a subclass constructor body runs.
        AuthoredMembers members = new(content.Document, placed, _authoredAssets);
        content.Apply?.Invoke(this, members);
        members.Finish();
    }

    /// <summary>
    /// The camera framing this scene. A scene always has one, and installing another cuts to it.
    /// </summary>
    /// <remarks>
    /// A camera installed before the scene starts becomes the opening camera. One installed later runs its
    /// <see cref="Scenes.Camera.OnStart"/> immediately.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The camera already frames another scene.</exception>
    [Authorable]
    public Camera Camera
    {
        get => _camera;
        protected set
        {
            ArgumentNullException.ThrowIfNull(value);
            RequireUnowned(value);

            Camera outgoing = _camera;
            _camera = value;

            // Installing the current camera is a cut.
            if (ReferenceEquals(outgoing.SceneOrNull, this) && !ReferenceEquals(outgoing, value))
            {
                outgoing.SceneOrNull = null;
                Install(value);
            }

            _camera.SavePrevious();
        }
    }

    /// <summary>
    /// Everything in this scene that can be collided with. A <see cref="Collider2D"/> or a
    /// <see cref="TileMapCollider2D"/> registers here when its entity joins the scene.
    /// </summary>
    /// <remarks>Game code queries this world directly for rays, sweeps and overlaps.</remarks>
    public CollisionWorld2D Collision { get; } = new();

    /// <summary>
    /// The run that owns this scene's lifetime state and requests. It is installed before the scene
    /// starts and is shared by every scene the run opens.
    /// </summary>
    /// <exception cref="InvalidOperationException">The run has not been installed yet.</exception>
    public Run Run
    {
        get => _run ?? throw new InvalidOperationException($"A {GetType().Name} has not started, so {NoRunYet}");
        internal set => _run = value;
    }

    // The run or null for components that start before the scene does.
    internal Run? RunOrNull => _run;

    // The ordinal the next particle emitter added to this scene draws its randomness stream from: a
    // static counter is not deterministic across runs, and a draw from Run.Random would shift gameplay
    // whenever an effect is added.
    internal ulong NextParticleStream() => _nextParticleStream++;

    /// <summary>
    /// World units the scene spans from its origin at (0, 0). Zero unless the scene sets it.
    /// </summary>
    /// <remarks>
    /// A scene composed from a document spans its authored size, or else its tile maps' largest
    /// dimensions.
    /// </remarks>
    [Authorable]
    public Vector2 Size { get; protected set; }

    /// <summary>The colour behind everything the scene draws, defaulting to black.</summary>
    [Authorable]
    public ColorRgba ClearColor { get; protected set; } = ColorRgba.Black;

    /// <summary>
    /// The colour the world is lit by where no light reaches, defaulting to white. White draws the
    /// world at its authored colour.
    /// </summary>
    /// <remarks>
    /// A light adds to it and brightens up to twice that colour. The screen layer is never lit.
    /// </remarks>
    [Authorable]
    public ColorRgba Ambient { get; protected set; } = ColorRgba.White;

    /// <summary>
    /// The sampling for world-space textures. A scene that sets none takes <see cref="Run.Sampling"/>
    /// when it starts, and reads <see cref="TextureSampling.Linear"/> before then.
    /// </summary>
    [Authorable]
    public TextureSampling Sampling
    {
        get => _sampling ?? TextureSampling.Linear;
        protected set => _sampling = value;
    }

    /// <summary>
    /// Whether world-layer renderers in the same draw band draw in order of their root entity's Y, lower
    /// first. Off by default.
    /// </summary>
    /// <remarks>
    /// The sort point is the root entity's origin: its simulated <see cref="Entity.Position"/>, not the
    /// interpolated one a frame draws. A root's whole subtree sorts with it, and a character and its
    /// shadow move through the order as one. The band always wins. A child with its own
    /// <see cref="Entity.ZIndex"/> sorts by its root's Y within its own band. Equal Y keeps the order
    /// the scene draws in with sorting off. The screen layer is not sorted.
    /// </remarks>
    /// <exception cref="InvalidOperationException">It is changed inside a <see cref="Renderer.Draw"/>.</exception>
    public bool YSort
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            ThrowIfDrawing("changed YSort");
            field = value;
            _renderIndex.Invalidate();
        }
    }

    /// <summary>
    /// Whether the scene holds the entities whose <see cref="Entity.StepMode"/> resolves to
    /// <see cref="StepMode.Pausable"/>, from the next step until it is cleared.
    /// </summary>
    /// <remarks>
    /// The scene and its camera always step, and <see cref="StepContext.Tick"/> and
    /// <see cref="StepContext.TotalSeconds"/> keep counting. A timer that should hold counts its own
    /// steps, as <see cref="Capsule.Animation.Countdown"/> does. A held entity skips its and its
    /// components' steps and late steps, and its colliders and screen notifiers settle nothing until it
    /// resumes. It still draws, collides with others, keeps its sounds and runs its structural hooks.
    /// Removing it, or disabling or detaching its collider or notifier, still raises the exits it owes.
    /// </remarks>
    public bool Paused { get; set; }

    /// <summary>State supplied by the transition that opened this scene.</summary>
    protected object? EntryPayload { get; private set; }

    /// <summary>
    /// Every entity held in step order: roots in addition order, each followed by its subtree. The span
    /// is invalid once an entity is added, removed or reparented.
    /// </summary>
    public ReadOnlySpan<Entity> Entities => _tree.Held;

    /// <summary>
    /// Adds a root entity and its subtree. During a step the add is deferred to the end of the
    /// step.
    /// </summary>
    /// <remarks>
    /// An entity with a parent joins through that parent and is refused here.
    /// <para>
    /// A component may refuse the scene from its entry hook. The entity stays in the scene with the
    /// components before it registered and the rest unregistered. If the add was deferred, the
    /// refusal surfaces at the queue drain instead of from this call.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The scene has stopped, the entity is already in a scene or queued, the entity has a parent,
    /// a component refused the scene, or it is called inside a <see cref="Renderer.Draw"/>.
    /// </exception>
    public void Add(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.Parent is { } parent)
        {
            throw new InvalidOperationException(
                $"A {entity.GetType().Name} is parented under a {parent.GetType().Name}. Add the root of the tree instead.");
        }

        Enqueue(entity);
    }

    /// <summary>
    /// Removes an entity and its subtree. During a step the remove is deferred and idempotent.
    /// </summary>
    /// <remarks>
    /// An entity still queued for addition attaches and detaches in the same drain, with matching
    /// hooks. Children detach first, deepest and last-parented first. A child removed by itself
    /// releases its <see cref="Entity.Parent"/> and becomes a root. Every removal hook runs, and
    /// failures propagate once detachment finishes. The entity reads <see cref="Entity.IsRemovalPending"/>
    /// until the remove lands.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The scene has stopped, the entity is not in it, or it is called inside a <see cref="Renderer.Draw"/>.
    /// </exception>
    public void Remove(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ThrowIfStopped();
        ThrowIfDrawing("removed an entity");

        if (!ReferenceEquals(entity.SceneOrNull, this) && !_pendingAddSet.Contains(entity))
        {
            throw new InvalidOperationException($"This scene does not hold the {entity.GetType().Name} being removed.");
        }

        if (_stepping)
        {
            if (_pendingRemoveSet.Add(entity))
            {
                _pendingRemoves.Add(entity);
            }

            return;
        }

        Detach(entity);
    }

    /// <summary>
    /// Holds the entities <see cref="Paused"/> would hold for the next <paramref name="ticks"/> steps,
    /// then resumes them by itself.
    /// </summary>
    /// <remarks>
    /// The count starts at the next step. A call made during a step, from a step, a late step or a
    /// contact handler, never holds the step in progress. A freeze already running keeps the longer of
    /// the two remaining counts, and zero changes nothing.
    /// </remarks>
    /// <param name="ticks">Fixed steps to hold, counted whether or not the scene is paused.</param>
    public void Freeze(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        _freezeTicks = Math.Max(_freezeTicks, ticks);
    }

    /// <summary>
    /// The first entity in <see cref="Entities"/> assignable to <typeparamref name="T"/>, or null when
    /// there is none.
    /// </summary>
    public T? FindFirst<T>()
        where T : class
    {
        foreach (T found in FindAll<T>())
        {
            return found;
        }

        return null;
    }

    /// <summary>
    /// The first entity in <see cref="Entities"/> assignable to <typeparamref name="T"/> for which
    /// <paramref name="match"/> returns true, or null when there is none.
    /// </summary>
    /// <remarks>It scans the whole scene. Call it at start or on an event, not every step.</remarks>
    public T? FindFirst<T>(Func<T, bool> match)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(match);

        foreach (T found in FindAll<T>())
        {
            if (match(found))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Every entity in <see cref="Entities"/> assignable to <typeparamref name="T"/>, in step order.</summary>
    /// <remarks>
    /// It walks the scene as it stands. An add or remove during a step lands when the step ends. Outside
    /// a step an add lands at once and ends the walk with an <see cref="InvalidOperationException"/>. So does
    /// a removal outside a step once any query compacts the list.
    /// </remarks>
    /// <example>
    /// <code>
    /// foreach (IEnemy enemy in FindAll&lt;IEnemy&gt;())
    /// {
    ///     enemy.Alert();
    /// }
    /// </code>
    /// </example>
    public EntityWalk<T> FindAll<T>()
        where T : class
    {
        _tree.Compact();
        return new(this);
    }

    /// <summary>The only entity in <see cref="Entities"/> assignable to <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">There is not exactly one matching entity.</exception>
    public T FindSingle<T>()
        where T : class
    {
        T? found = null;

        foreach (T candidate in FindAll<T>())
        {
            if (found is not null)
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} holds more than one entity assignable to {typeof(T).Name}.");
            }

            found = candidate;
        }

        return found ?? throw new InvalidOperationException(
            $"A {GetType().Name} holds no entity assignable to {typeof(T).Name}.");
    }

    /// <summary>
    /// The pool of <typeparamref name="T"/> that every spawner in this scene shares. The scene builds it
    /// from its declarations, preloads it before it starts, and returns its entities when they leave or
    /// the scene stops.
    /// </summary>
    /// <remarks>
    /// What takes from the pool declares it with <see cref="AssetCollectionExtensions.Pool{T}"/> from its
    /// <c>CollectAssets</c>, which states how declarations add up. A pool no declaration reached builds
    /// one entity at its first call, and its first take logs once at <see cref="Log.Info"/> that its
    /// assets were not preloaded.
    /// <para>
    /// A taken entity is a new life. Everything per-life, tuning included, is set after <c>Take</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void CollectAssets(AssetCollection assets) =&gt; assets.Pool&lt;SparkBurst&gt;(capacity: 8);
    ///
    /// protected override void OnStep(in StepContext context)
    /// {
    ///     if (_life.IsRunning) return;
    ///     Scene.Add(Scene.Pool&lt;SparkBurst&gt;().Take().Burst(Position));
    ///     Scene.Remove(this);
    /// }
    /// </code>
    /// </example>
    /// <typeparam name="T">The pooled entity type, built with no arguments.</typeparam>
    public EntityPool<T> Pool<T>()
        where T : Entity, new()
    {
        if (_pools.TryGetValue(typeof(T), out IEntityPool? pool))
        {
            return (EntityPool<T>)pool;
        }

        return BuildPool<T>(capacity: 1);
    }

    // A declaration from a CollectAssets hook. A pool holds the sum of one collection's declarations.
    // A pooled entity's declaration of its own pool, or of a pool its pool grew from, is ancestral. The
    // ancestral declarations count once, as the largest of them. A cycle of declarations then settles.
    internal void DeclarePool<T>(int capacity)
        where T : Entity, new()
    {
        _pools.TryGetValue(typeof(T), out IEntityPool? existing);
        IEntityPool? declarer = _declaringPool;
        bool ancestral = existing is not null && declarer is not null && GrewFrom(declarer, existing);

        _declared.TryGetValue(typeof(T), out PoolDeclarations declared);
        declared = ancestral
            ? declared with { Ancestral = Math.Max(declared.Ancestral, capacity) }
            : declared with { Summed = declared.Summed + capacity };
        _declared[typeof(T)] = declared;

        int total = declared.Summed + declared.Ancestral;
        EntityPool<T> pool = existing as EntityPool<T> ?? BuildPool<T>(total);
        pool.Reserve(total);

        if (declarer is not null && !ancestral)
        {
            if (!_grownBy.TryGetValue(pool, out HashSet<IEntityPool>? growers))
            {
                growers = [];
                _grownBy.Add(pool, growers);
            }

            growers.Add(declarer);
        }
    }

    // Whether this is the running collection's first reach of entity.
    internal bool FirstCollected(Entity entity) => _collected.Add(entity);

    // Names the shared pool whose entity declares next, or keeps the current one for an entity outside
    // every shared pool. Returns the one it replaces for RestoreDeclaringPool.
    internal IEntityPool? EnterDeclaringPool(IEntityPool? shared)
    {
        IEntityPool? previous = _declaringPool;
        _declaringPool = shared ?? previous;
        return previous;
    }

    internal void RestoreDeclaringPool(IEntityPool? previous) => _declaringPool = previous;

    // Whether pool is target or grew from target through the declarations recorded so far. The recursion
    // ends because the graph has no cycle: DeclarePool adds an edge only where this returned false.
    private bool GrewFrom(IEntityPool pool, IEntityPool target)
    {
        if (ReferenceEquals(pool, target))
        {
            return true;
        }

        if (_grownBy.TryGetValue(pool, out HashSet<IEntityPool>? growers))
        {
            foreach (IEntityPool grower in growers)
            {
                if (GrewFrom(grower, target))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private EntityPool<T> BuildPool<T>(int capacity)
        where T : Entity, new()
    {
        EntityPool<T> pool = new(static () => new T(), capacity) { Shared = true };
        _pools.Add(typeof(T), pool);
        _poolOrder.Add(pool);
        return pool;
    }

    /// <summary>
    /// The <see cref="Collider2D"/> a query hit names, or null when it names a tile map's grid, nothing,
    /// or a collider since removed.
    /// </summary>
    /// <remarks>
    /// Every hit a query returns names what it met as a <see cref="CollisionTarget.Collider"/> handle in
    /// <see cref="Collision"/>. The collider's <see cref="Component.Entity"/> is the entity behind it.
    /// </remarks>
    /// <example>
    /// Finding the trigger entities a collider overlaps:
    /// <code>
    /// Span&lt;Contact2D&gt; found = stackalloc Contact2D[8];
    /// int count = Math.Min(probe.OverlapAll(TriggerLayer, found), found.Length);
    /// for (int index = 0; index &lt; count; index++)
    /// {
    ///     if (Scene.ColliderOf(found[index].Target.Collider) is { Entity: CameraTrigger trigger })
    ///     {
    ///         Enter(trigger);
    ///     }
    /// }
    /// </code>
    /// </example>
    public Collider2D? ColliderOf(ColliderHandle handle) => Collision.UserDataOrNull(handle) as Collider2D;

    /// <summary>The tile-map cell a query hit names, or null when it names anything else.</summary>
    /// <example>
    /// Breaking the brick a shot meets:
    /// <code>
    /// if (Scene.Collision.Raycast(Position, direction, reach, Solid, out RayHit2D hit)
    ///     &amp;&amp; Scene.TileOf(hit.Target) is { Type: Brick } brick)
    /// {
    ///     brick.Map.RemoveTile(brick.X, brick.Y);
    /// }
    /// </code>
    /// </example>
    public TileContact2D? TileOf(in CollisionTarget target) =>
        TileContact2D.Of(Collision.UserDataOrNull(target.Collider), target);

    /// <summary>
    /// Runs before the scene's first frame is built, which is when the camera opens. A scene belongs
    /// to a single <see cref="SceneSimulation"/> for its lifetime and never starts twice.
    /// </summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once before the scene releases its entities, when it is replaced or its host ends.
    /// </summary>
    protected virtual void OnStop()
    {
    }

    /// <summary>Runs after the previous positions are saved and before entities step.</summary>
    protected virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Runs after every entity's <see cref="Entity.OnLateStep"/> and before the frame is built.
    /// </summary>
    /// <remarks>
    /// Set the scene's camera policy here. The camera's own <see cref="Scenes.Camera.OnLateStep"/>
    /// and its follow run afterwards and frame the result.
    /// </remarks>
    protected virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>
    /// Draws the scene's own debug geometry, as <see cref="Component.OnDebugDraw"/> describes.
    /// Draws nothing by default.
    /// </summary>
    /// <remarks>The camera, the entities and their components draw after it.</remarks>
    protected virtual void OnDebugDraw()
    {
    }

    /// <summary>
    /// Fills the scene's section of the debug overlay's scene page, as
    /// <see cref="Component.OnDebugPanel"/> describes. Writes nothing by default.
    /// </summary>
    /// <remarks>
    /// The run's seed, <see cref="Size"/>, <see cref="ClearColor"/>, <see cref="Ambient"/>,
    /// <see cref="Sampling"/> and <see cref="Paused"/> are written before this call, and the scene's
    /// entities are listed after it. A Camera row opens <see cref="Camera.OnDebugPanel"/>.
    /// </remarks>
    protected virtual void OnDebugPanel(DebugPanel panel)
    {
    }

    /// <summary>
    /// Appends assets this scene declares beyond those owned by its entities and components.
    /// Collection can run before <see cref="OnStart"/>.
    /// </summary>
    /// <remarks>
    /// Declare from construction-time state. An override appends to <paramref name="assets"/> and
    /// changes nothing else.
    /// </remarks>
    protected internal virtual void CollectAssets(AssetCollection assets)
    {
    }

    /// <summary>
    /// Returns a new collection of every asset this scene and its entities declare now, as the scene
    /// boundary would preload them.
    /// </summary>
    /// <remarks>
    /// A scene read before it starts reports what its boundary loads. The read runs the
    /// <c>CollectAssets</c> hooks, loads nothing and counts as no pool's forwarding.
    /// </remarks>
    /// <example>
    /// <code>
    /// Assert.True(new Room().CollectPreloads().Contains(CapsuleAssets.Textures.BoltTexture));
    /// </code>
    /// </example>
    public AssetCollection CollectPreloads()
    {
        AssetCollection assets = new() { IsProbe = true };
        Collect(assets);
        return assets;
    }

    internal AssetCollection CollectAssetPreloads()
    {
        _preloadsCollected = true;
        AssetCollection assets = new();
        Collect(assets);
        return assets;
    }

    private void Collect(AssetCollection assets)
    {
        assets.GatheringScene = this;
        try
        {
            CollectAssets(assets);
            if (_authoredAssets is { } authored)
            {
                assets.Add(authored);
            }

            foreach (Entity entity in Entities)
            {
                entity.CollectAssetPreloads(assets);
            }

            // A pooled entity's declaration can build a pool or grow one already walked. The walk repeats
            // until a pass builds nothing. It ends because each entity declares only on its first reach.
            int built;
            do
            {
                built = PooledEntityCount();
                for (int index = 0; index < _poolOrder.Count; index++)
                {
                    _poolOrder[index].CollectAssets(assets);
                }
            }
            while (PooledEntityCount() != built);

            if (!assets.IsProbe)
            {
                ReservePooled();
            }
        }
        finally
        {
            assets.GatheringScene = null;
            _declared.Clear();
            _collected.Clear();
            _grownBy.Clear();
            _declaringPool = null;
        }
    }

    private int PooledEntityCount()
    {
        int count = 0;
        foreach (IEntityPool pool in _poolOrder)
        {
            count += pool.Capacity;
        }

        return count;
    }

    // Sizes what the scene grows as entities attach and leave for every collected entity held at once.
    // A pooled entity's first attach and removal then allocate nothing.
    private void ReservePooled()
    {
        int entities = _tree.Slots.Count;
        int reporters = _contactReporters.Count;
        int notifiers = _screenNotifiers.Count;
        int colliders = Collision.ColliderCount;
        int grids = Collision.Grids.Length;
        int renderers = 0;
        foreach (Entity entity in _collected)
        {
            // The counts above hold what the scene holds already. The draw order counts every renderer.
            bool joins = entity.SceneOrNull != this;
            entities += joins ? 1 : 0;
            foreach (Component component in entity.Components)
            {
                switch (component)
                {
                    case Renderer:
                        renderers++;
                        break;
                    case Collider2D collider when joins:
                        colliders++;
                        reporters += collider.ReportsContacts ? 1 : 0;
                        break;
                    case TileMapCollider2D when joins:
                        colliders++;
                        grids++;
                        break;
                    case VisibleOnScreenNotifier2D when joins:
                        notifiers++;
                        break;
                }
            }
        }

        _tree.Reserve(entities);
        _detachingRoots.EnsureCapacity(entities);
        _pendingAdds.EnsureCapacity(entities);
        _pendingAddSet.EnsureCapacity(entities);
        _pendingStarts.EnsureCapacity(entities);
        _pendingRemoves.EnsureCapacity(entities);
        _pendingRemoveSet.EnsureCapacity(entities);
        _contactReporters.Reserve(reporters);
        _screenNotifiers.Reserve(notifiers);
        Collision.Reserve(colliders, grids);
        _renderIndex.Reserve(renderers);
    }

    internal void Start(object? entryPayload)
    {
        if (_started)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} has already started. Build a new scene for a second simulation.");
        }

        _started = true;
        EntryPayload = entryPayload;

        // The game default fills in behind the scene's own setting.
        _sampling ??= Run.Sampling;

        // A host with nothing to load still collects. Forwarding a pool is recorded before any start
        // hook can take from it.
        if (!_preloadsCollected)
        {
            CollectAssetPreloads();
        }

        // Attach everything composed at construction before any of it starts.
        StartPending();

        // Install the camera after starts so it finds entities that have started.
        Install(_camera);

        OnStart();

        // The frame drawn before the first step shows the subject where every start hook left it.
        Camera.CutToSubject();

        // A scene's first frame never interpolates.
        Camera.SavePrevious();
    }

    internal void Stop()
    {
        if (!_started || _stopped)
        {
            return;
        }

        _stopped = true;
        List<Exception>? failures = null;

        try
        {
            OnStop();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        // Release the camera before the entities it framed.
        if (ReferenceEquals(_camera.SceneOrNull, this))
        {
            _camera.SceneOrNull = null;
        }

        ReleaseEntities(ref failures);
        ClearPendingState();
        ThrowCleanupFailures(failures);
    }

    // Release a composed scene that was rejected before it started. Structural removal hooks run,
    // temporal ones do not.
    internal void Abandon()
    {
        if (_started)
        {
            throw new InvalidOperationException($"A started {GetType().Name} must be stopped, not abandoned.");
        }

        if (_stopped)
        {
            return;
        }

        _stopped = true;
        List<Exception>? failures = null;
        ReleaseEntities(ref failures);
        ClearPendingState();
        ThrowCleanupFailures(failures);
    }

    // Throw a single failure or all aggregated. Cleanup runs to completion before throwing.
    internal static void ThrowCleanupFailures(List<Exception>? failures)
    {
        if (failures is [Exception failure])
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more scene cleanup hooks failed.", failures);
        }
    }

    internal void InvalidateRenderers() => _renderIndex.Invalidate();

    // Rewrites view from the scene as it stands, calling every visible renderer's Draw in draw order.
    // A change to the scene from inside Draw throws. A frame is then the same however often it is built.
    // A host's camera, when one is passed, frames it in place of the scene's.
    internal void DrawFrame(FrameView view, CameraView? camera = null)
    {
        view.Clear();
        view.Camera = camera ?? Camera.ToView();
        view.Canvas = Run.Canvas;
        view.ClearColor = ClearColor;
        view.Ambient = Ambient;
        view.Sampling = Sampling;

        _drawing = true;
        try
        {
            ReadOnlySpan<Renderer> renderers = _renderIndex.GetDrawOrder(Entities, YSort);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!renderer.Visible)
                {
                    continue;
                }

                // The only place the render space, scroll factor, tint, flash and material are chosen.
                Entity entity = renderer.Entity!;
                if (entity.TryGetDrawStyle(out ColorRgba tint, out ColorRgba flash))
                {
                    view.Space = entity.Space;
                    view.ScrollFactor = entity.ScrollFactor;
                    view.SetStyle(tint, flash);
                    view.Material = renderer.Material;
                    renderer.Draw(view);
                }
            }
        }
        finally
        {
            view.Space = RenderSpace.World;
            view.ScrollFactor = Vector2.One;
            view.SetStyle(ColorRgba.White, default);
            view.Material = null;
            _drawing = false;
        }
    }

    // Guards every change that reaches the draw order. The draw pass holds no deferral queue.
    internal void ThrowIfDrawing(string change)
    {
        if (_drawing)
        {
            throw new InvalidOperationException(
                $"A Renderer.Draw {change} in a {GetType().Name}. Draw only writes the FrameView it is handed. Make the change in a Step or LateStep.");
        }
    }

    // Held and not queued for removal, by itself or with an ancestor.
    internal bool Contains(Entity entity) => ReferenceEquals(entity.SceneOrNull, this) && !IsRemovalPending(entity);

    // Whether Remove was called on the entity itself this step.
    internal bool IsRemovalRequested(Entity entity) => _pendingRemoveSet.Count > 0 && _pendingRemoveSet.Contains(entity);

    internal bool IsDetaching(Entity entity)
    {
        if (_detachingRoots.Count == 0)
        {
            return false;
        }

        for (Entity? above = entity; above is not null; above = above.Parent)
        {
            foreach (Entity root in _detachingRoots)
            {
                if (ReferenceEquals(above, root))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Whether a removal queued this step takes the entity, directly or through an ancestor.
    internal bool IsRemovalPending(Entity entity)
    {
        if (_pendingRemoveSet.Count == 0)
        {
            return false;
        }

        for (Entity? above = entity; above is not null; above = above.Parent)
        {
            if (_pendingRemoveSet.Contains(above))
            {
                return true;
            }
        }

        return false;
    }

    internal void BeginStep()
    {
        _stepping = true;
        StepsBegun++;

        // Pause and freeze settle once here, so no step is held for only part of the tree.
        bool paused = Paused;
        bool frozen = _freezeTicks > 0;
        if (frozen)
        {
            _freezeTicks--;
        }

        bool resolve = _holdsStale || paused != _pausedThisStep || frozen != _frozenThisStep;
        _holdsStale = false;
        _pausedThisStep = paused;
        _frozenThisStep = frozen;

        Camera.SavePrevious();

        foreach (Entity entity in Entities)
        {
            if (resolve)
            {
                entity.ResolveHold(paused, frozen);
            }

            entity.SavePrevious();
        }

        // Notifiers that joined between steps settle against the last frame before any entity steps, so each first
        // step reads that frame as one landing with a step's deferred adds does. A held one waits for the step its
        // entity first runs. The walk restarts after each settle, as a handler may detach another.
        for (int index = 0; index < _arrivals.Count;)
        {
            VisibleOnScreenNotifier2D notifier = _arrivals[index];
            if (notifier.Entity!.Held)
            {
                index++;
                continue;
            }

            _arrivals.RemoveAt(index);
            notifier.SettleVisibility(_settledRegion);
            index = 0;
        }
    }

    // Makes the next step resolve every entity's hold again.
    internal void InvalidateHolds() => _holdsStale = true;

    internal void RunStep(in StepContext context)
    {
        SteppingTick = context.Tick;
        OnStep(context);
    }

    internal void StepEntities(in StepContext context)
    {
        foreach (Entity entity in Entities)
        {
            entity.RunStep(context);
        }
    }

    internal void LateStepEntities(in StepContext context)
    {
        foreach (Entity entity in Entities)
        {
            entity.RunLateStep(context);
        }
    }

    internal void SettleContacts()
    {
        _contactReporters.Begin();
        try
        {
            while (_contactReporters.TryNext(out Collider2D? collider))
            {
                if (!collider.Entity!.Held)
                {
                    collider.SettleContacts();
                }
            }
        }
        finally
        {
            _contactReporters.End();
        }
    }

    internal void TrackContacts(Collider2D collider) => _contactReporters.Add(collider);

    internal void NoteSink(float sink) => DeepestSink = MathF.Max(DeepestSink, sink);

    // Adds or withdraws one body's moved-by layers from the scene's union.
    internal void CountMovedBy(CollisionFilter filter, int delta)
    {
        ulong bits = filter.Bits;
        while (bits != 0)
        {
            int layer = BitOperations.TrailingZeroCount(bits);
            ulong bit = 1UL << layer;
            bits &= bits - 1;

            _movedByCounts[layer] += delta;
            MovedByLayers = _movedByCounts[layer] > 0 ? MovedByLayers | bit : MovedByLayers & ~bit;
        }
    }

    internal void UntrackContacts(Collider2D collider) => _contactReporters.Remove(collider);

    internal void RunLateStep(in StepContext context)
    {
        OnLateStep(context);
        Camera.OnLateStep(context);

        // The frame's visible region is final. Notifiers settle against it after deferred adds land.
        Camera.Settle(context);
        _settledRegion = Camera.VisibleRegion;
    }

    // A notifier joining between steps waits for the next step to begin. One landing with a step's deferred adds
    // settles after the drain.
    internal void TrackVisibility(VisibleOnScreenNotifier2D notifier)
    {
        _screenNotifiers.Add(notifier);
        if (!_stepping)
        {
            _arrivals.Add(notifier);
        }
    }

    internal void UntrackVisibility(VisibleOnScreenNotifier2D notifier)
    {
        _screenNotifiers.Remove(notifier);
        _arrivals.Remove(notifier);
    }

    // Draw the scene, camera, and entities in step order after the step settles and deferred adds land.
    internal void RunDebugDraw()
    {
        OnDebugDraw();
        Camera.OnDebugDraw();

        foreach (Entity entity in Entities)
        {
            entity.RunDebugDraw();
        }
    }

    // Fill the scene page's section. The hook runs only if the scene has started.
    internal void RunDebugPanel(DebugPanel panel)
    {
        panel.Section("Scene");
        panel.Field("Seed", Run.Random.Seed.ToString(CultureInfo.InvariantCulture));
        panel.Field("Size", Size);
        panel.Field("ClearColor", ClearColor);
        panel.Field("Ambient", Ambient);
        panel.Field("Sampling", Sampling);
        panel.Field("Paused", Paused);

        if (_started)
        {
            OnDebugPanel(panel);
        }
    }

    internal void EndStep()
    {
        try
        {
            _drained = true;
            DrainPending();
            SettleNotifiers();
        }
        finally
        {
            // The step is over however the drain went.
            _stepping = false;
            _drained = false;
            SteppingTick = null;
        }
    }

    // Drain queues while lifecycle hooks grow them. Dropped entities are not retried next step.
    private void DrainPending()
    {
        if (_tree.Stale)
        {
            RebuildOrder();
        }

        while (_pendingAdds.Count > 0 || _pendingRemoves.Count > 0 || _pendingStarts.Count > 0)
        {
            int processed = 0;
            try
            {
                while (processed < _pendingAdds.Count)
                {
                    Entity pending = _pendingAdds[processed];

                    // Counted before the attach. A failed attach is then not retried.
                    processed++;
                    Attach(pending);
                }
            }
            finally
            {
                DropProcessed(_pendingAdds, _pendingAddSet, processed);
            }

            processed = 0;
            try
            {
                while (processed < _pendingRemoves.Count)
                {
                    Entity pending = _pendingRemoves[processed];
                    processed++;
                    Detach(pending);
                }
            }
            finally
            {
                DropProcessed(_pendingRemoves, _pendingRemoveSet, processed);
            }

            // Start pending after draining both queues so the batch starts together.
            StartPending();
        }
    }

    // Settle registered notifiers against the frame region. One registered during the walk waits
    // for the next step.
    private void SettleNotifiers()
    {
        _screenNotifiers.Begin();
        try
        {
            while (_screenNotifiers.TryNext(out VisibleOnScreenNotifier2D? notifier))
            {
                if (!notifier.Entity!.Held)
                {
                    notifier.SettleVisibility(_settledRegion);
                }
            }
        }
        finally
        {
            _screenNotifiers.End();
        }
    }

    // Enqueue for addition. A child parented under a held entity joins directly.
    internal void Enqueue(Entity entity)
    {
        ThrowIfStopped();
        ThrowIfDrawing("added an entity");

        if (entity.IdleInPool)
        {
            throw new InvalidOperationException(
                $"This {entity.GetType().Name} is idle in its pool. Take it from the pool instead of adding it.");
        }

        if (entity.SceneOrNull is not null || _pendingAddSet.Contains(entity))
        {
            throw new InvalidOperationException(
                $"A {entity.GetType().Name} is already in a scene. Remove it before adding it elsewhere.");
        }

        if (_stepping)
        {
            _pendingAdds.Add(entity);
            _pendingAddSet.Add(entity);
            entity.PendingScene = this;
            return;
        }

        Attach(entity);
    }

    private static void DropProcessed(List<Entity> queue, HashSet<Entity> membership, int processed)
    {
        for (int index = 0; index < processed; index++)
        {
            membership.Remove(queue[index]);
            queue[index].PendingScene = null;
        }

        queue.RemoveRange(0, processed);
    }

    private void Attach(Entity entity)
    {
        // Idempotent: the drain may hand over an entity that already attached.
        if (entity.SceneOrNull is not null)
        {
            return;
        }

        _attaching++;
        try
        {
            AttachTree(entity);
        }
        finally
        {
            _attaching--;
        }

        // Start immediately if attached outside a step. During a step, EndStep starts all together.
        // A join hook that adds more waits for the outermost attach, so the starts sort parent-first.
        if (_started && !_stepping && _attaching == 0)
        {
            StartPending();
        }
    }

    private void AttachTree(Entity entity)
    {
        _tree.Insert(entity);
        _renderIndex.Invalidate();
        entity.SceneOrNull = this;
        entity.PendingScene = null;

        // Resolved against this step's state, so a join during the step settles its screen notifiers
        // as the rest of the tree does.
        entity.ResolveHold(_pausedThisStep, _frozenThisStep);

        // A join never interpolates. Parent first, so a child's World composes against a parent
        // already collapsed onto its current transform.
        entity.SavePrevious();

        // Enter components before the entity hook. Components attached from OnAddedToScene notify separately.
        entity.EnterScene();
        entity.OnAddedToScene();

        _pendingStarts.Add(entity);

        // A hook may add or remove a child, so the index advances only while its slot still holds the one just visited.
        int child = 0;
        while (child < entity.Children.Length)
        {
            Entity next = entity.Children[child];
            if (next.SceneOrNull is null)
            {
                AttachTree(next);
            }

            if (child < entity.Children.Length && ReferenceEquals(entity.Children[child], next))
            {
                child++;
            }
        }
    }

    // The step walk reads the list, so a rebuild during it waits for the drain.
    internal void Reparented(Entity entity, bool wasRoot)
    {
        InvalidateHolds();
        _tree.Reparented(entity, wasRoot);

        if (!_stepping || _drained)
        {
            RebuildOrder();
        }
    }

    private void RebuildOrder()
    {
        _tree.Rebuild();
        _renderIndex.Invalidate();
    }

    // OnStart may attach more, and this loop drains those too.
    private void StartPending()
    {
        if (_starting)
        {
            return;
        }

        _starting = true;

        try
        {
            int processed = 0;
            int sortedVersion = -1;
            try
            {
                while (processed < _pendingStarts.Count)
                {
                    // Joins, rebuilds and hooks move entities that wait, and starts run parent-first. A slot
                    // moves only with the version, so a batch with no moves sorts once.
                    if (sortedVersion != _tree.Version)
                    {
                        sortedVersion = _tree.Version;
                        if (_pendingStarts.Count - processed > 1)
                        {
                            CollectionsMarshal.AsSpan(_pendingStarts)[processed..].Sort(static (left, right) => left.SceneSlot.CompareTo(right.SceneSlot));
                        }
                    }

                    Entity pending = _pendingStarts[processed];
                    processed++;

                    // Skip entities that attached and detached or were removed before their turn.
                    if (Contains(pending))
                    {
                        pending.RunStart();
                    }
                }

                // A join never interpolates, and that holds for wherever the batch's start hooks left
                // each entity. The whole batch has started, so a start that moved a peer is covered too.
                for (int index = 0; index < processed; index++)
                {
                    if (Contains(_pendingStarts[index]))
                    {
                        _pendingStarts[index].SavePrevious();
                    }
                }
            }
            finally
            {
                _pendingStarts.RemoveRange(0, processed);
            }
        }
        finally
        {
            _starting = false;
        }
    }

    private void Install(Camera camera)
    {
        RequireUnowned(camera);
        camera.SceneOrNull = this;
        camera.RunStart();
    }

    private void RequireUnowned(Camera camera)
    {
        if (camera.SceneOrNull is not null && !ReferenceEquals(camera.SceneOrNull, this))
        {
            throw new InvalidOperationException(
                $"A {camera.GetType().Name} is already framing another scene. Install a different camera.");
        }
    }

    // Detach the entity and release its parent so it can be reparented or added as a root.
    private void Detach(Entity entity)
    {
        _detachingRoots.Add(entity);
        try
        {
            DetachTree(entity);
            entity.Unparent();
        }
        finally
        {
            _detachingRoots.RemoveAt(_detachingRoots.Count - 1);
        }
    }

    // Detach children before the entity, deepest and last-parented first. A hook may reparent, so
    // the list is re-read on every pass.
    private void DetachTree(Entity entity)
    {
        for (int child = entity.Children.Length - 1; child >= 0; child--)
        {
            if (child < entity.Children.Length)
            {
                DetachTree(entity.Children[child]);
            }
        }

        if (ReferenceEquals(entity.SceneOrNull, this))
        {
            DetachAt(entity.SceneSlot);
        }
    }

    private void DetachAt(int index)
    {
        Entity entity = _tree.Vacate(index);

        _renderIndex.Invalidate();
        entity.Leaving = true;
        entity.SceneOrNull = null;

        try
        {
            entity.LeaveScene();
        }
        finally
        {
            entity.Leaving = false;

            // Returns even when a removal hook throws. A hook that re-added the entity leaves it
            // taken, and it returns on its next detach.
            if (entity.Pool is { } pool && entity.SceneOrNull is null && entity.PendingScene is null)
            {
                pool.Return(entity);
            }
        }
    }

    private void ReleaseEntities(ref List<Exception>? failures)
    {
        // A removal hook may read the scene and compact it. Each pass re-checks the slot.
        List<Entity> slots = _tree.Slots;
        for (int index = slots.Count - 1; index >= 0; index--)
        {
            if (index >= slots.Count || slots[index] is null)
            {
                continue;
            }

            try
            {
                DetachAt(index);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _tree.Compact();
    }

    private void ClearPendingState()
    {
        _pendingStarts.Clear();
        _pendingAdds.Clear();
        _pendingAddSet.Clear();
        _pendingRemoves.Clear();
        _pendingRemoveSet.Clear();
        _tree.Reset();
        _renderIndex.Clear();
        _contactReporters.Clear();
        _screenNotifiers.Clear();
    }

    private void ThrowIfStopped()
    {
        if (_stopped)
        {
            throw new InvalidOperationException($"A stopped {GetType().Name} cannot be changed or request another transition.");
        }
    }

    /// <summary>The entities of a scene assignable to <typeparamref name="T"/>, walked by <see langword="foreach"/>.</summary>
    /// <typeparam name="T">The class or interface the walk yields.</typeparam>
    public struct EntityWalk<T>
        where T : class
    {
        private readonly Scene _scene;
        private readonly int _version;
        private int _index;

        internal EntityWalk(Scene scene)
        {
            _scene = scene;
            _version = scene._tree.Version;
            _index = -1;
        }

        /// <summary>The entity the walk stands on.</summary>
        public readonly T Current => (T)(object)_scene._tree.Slots[_index];

        /// <summary>The walk itself, which <see langword="foreach"/> binds to.</summary>
        public readonly EntityWalk<T> GetEnumerator() => this;

        /// <summary>Steps to the next entity assignable to <typeparamref name="T"/>, or returns false past the last.</summary>
        /// <exception cref="InvalidOperationException">
        /// An entity was added outside a step since the walk began, or removed outside a step and then compacted away by a query.
        /// </exception>
        public bool MoveNext()
        {
            if (_version != _scene._tree.Version)
            {
                throw new InvalidOperationException(
                    $"A {_scene.GetType().Name}'s entities moved during a walk over them, which would skip or repeat one. " +
                    "Collect the entities to add or remove, and change the scene after the walk.");
            }

            List<Entity> entities = _scene._tree.Slots;
            while (++_index < entities.Count)
            {
                if (entities[_index] is T)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // A list that can be walked while its callbacks modify it. Entries added during a walk are
    // skipped until the next one.
    private sealed class SettleList<T>
        where T : class
    {
        private readonly List<T> _items = [];
        private int _next;
        private int _end;

        internal int Count => _items.Count;

        // Callers pair Add with Remove, so duplicates cannot build up.
        internal void Add(T item) => _items.Add(item);

        internal void Reserve(int capacity) => _items.EnsureCapacity(capacity);

        internal void Remove(T item)
        {
            for (int index = 0; index < _items.Count; index++)
            {
                if (!ReferenceEquals(_items[index], item))
                {
                    continue;
                }

                _items.RemoveAt(index);
                if (index < _next)
                {
                    _next--;
                    _end--;
                }
                else if (index < _end)
                {
                    _end--;
                }

                return;
            }
        }

        internal void Begin()
        {
            _next = 0;
            _end = _items.Count;
        }

        internal bool TryNext([NotNullWhen(true)] out T? item)
        {
            // The list may have been cleared by a callback.
            if (_next >= _end || _next >= _items.Count)
            {
                item = null;
                return false;
            }

            item = _items[_next++];
            return true;
        }

        internal void End()
        {
            _next = 0;
            _end = 0;
        }

        internal void Clear()
        {
            _items.Clear();
            End();
        }
    }

    // One shared pool's declarations in a collection: the sum of those that are not ancestral, and the
    // largest that is.
    private readonly record struct PoolDeclarations(int Summed, int Ancestral);
}

using System.Diagnostics.CodeAnalysis;
using Capsule.Assets;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

// The link an Entity carries back to the pool that owns it. Non-generic so Entity, which knows
// nothing of T, can hold one.
internal interface IEntityPool
{
    int Capacity { get; }

    bool Shared { get; }

    void Return(Entity entity);

    void CollectAssets(AssetCollection assets);
}

/// <summary>
/// Hands out <typeparamref name="T"/> entities built once and reused for the pool's life. The
/// engine returns an entity to this pool the moment its removal from a scene lands, or when the
/// scene stops.
/// </summary>
/// <remarks>
/// A spawner takes from its scene's pool of a type, <see cref="Scene.Pool{T}"/>, by default. It builds its own
/// pool only where a type cannot name the pool: its entities take constructor arguments, or one pool of a base
/// type is filled by several factories. The game writes no release call, and the pooled class holds no pool
/// reference. Idle entities sit outside any scene. The pool's owner forwards it from its own
/// <c>CollectAssets</c> through <see cref="CollectAssets"/> to preload what they draw and play. The first take
/// logs once at <see cref="Log.Info"/> when no preload collection has reached the pool and its entities hold
/// assets to preload.
/// </remarks>
/// <example>
/// <code>
/// private readonly EntityPool&lt;Walker&gt; _walkers;
///
/// public Gate(Path path)
///     : base(path.Start) =&gt; _walkers = new(() =&gt; new Walker(path), capacity: 4);
///
/// protected override void CollectAssets(AssetCollection assets) =&gt; _walkers.CollectAssets(assets);
///
/// private void Release() =&gt; Scene.Add(_walkers.Take().Place(Position));
/// </code>
/// </example>
/// <typeparam name="T">The pooled entity type.</typeparam>
public sealed class EntityPool<T> : IEntityPool
    where T : Entity
{
    private readonly Func<T> _create;
    private readonly Stack<T> _idle;

    // Every entity Build ever returned, idle or taken, for asset collection.
    private readonly List<T> _built;
    private int _sizedCapacity;
    private bool _loggedGrowth;

    // Whether CollectAssets has run outside a probe, and whether the first take has checked it.
    private bool _forwarded;
    private bool _checkedForward;

    // Set while this pool collects. An entity that forwards the pool it lives in stops here.
    private bool _collecting;

    // Set on a scene's shared pool, whose first-take log names the declaration that preloads it.
    internal bool Shared { get; init; }

    /// <summary>Builds <paramref name="capacity"/> entities now through <paramref name="create"/> and holds them idle.</summary>
    /// <param name="create">Builds one more entity, on demand. Every entity it ever returns is owned by this pool for life.</param>
    /// <param name="capacity">How many entities to build now. Positive.</param>
    /// <exception cref="InvalidOperationException"><paramref name="create"/> returned an entity already owned by a pool.</exception>
    public EntityPool(Func<T> create, int capacity)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        _create = create;
        _sizedCapacity = capacity;
        _idle = new Stack<T>(capacity);
        _built = new List<T>(capacity);

        for (int index = 0; index < capacity; index++)
        {
            _idle.Push(Build());
        }
    }

    /// <summary>Entities this pool has ever built, idle or taken.</summary>
    public int Capacity => _built.Count;

    /// <summary>Idle entities ready to hand out.</summary>
    public int Available => _idle.Count;

    /// <summary>Entities taken and not yet returned.</summary>
    public int Active => Capacity - Available;

    /// <summary>
    /// Pops the most recently returned idle entity, or builds one more when none is idle. Never
    /// null.
    /// </summary>
    /// <remarks>
    /// The entity returns to this pool when it leaves its scene.
    /// <para>
    /// The first build past the constructed capacity logs once at <see cref="Log.Debug"/>. Size the
    /// pool for its peak.
    /// </para>
    /// </remarks>
    public T Take()
    {
        if (TryTake(out T? entity))
        {
            return entity;
        }

        if (!_loggedGrowth)
        {
            _loggedGrowth = true;
            Log.Debug($"{typeof(T).Name} pool grew past {_sizedCapacity}. Size the pool for its peak");
        }

        T built = Build();
        built.IdleInPool = false;
        return built;
    }

    /// <summary>Pops an idle entity, or returns false without building one.</summary>
    public bool TryTake([NotNullWhen(true)] out T? entity)
    {
        CheckForwarded();
        if (!_idle.TryPop(out entity))
        {
            return false;
        }

        entity.IdleInPool = false;
        return true;
    }

    /// <summary>
    /// Appends the assets of every entity this pool has built, as a scene collects them for the
    /// entities it holds.
    /// </summary>
    /// <remarks>
    /// Call it from the <c>CollectAssets</c> override of the entity or scene that declares the pool.
    /// A pool nothing forwards preloads nothing, and its entities' textures and sounds load on first
    /// use mid-play. A scene's preload also reserves room for every entity a forwarded pool holds. Its
    /// first takes then attach and leave without allocating.
    /// </remarks>
    /// <example>
    /// <code>
    /// private EntityPool&lt;Bolt&gt; _bolts = new(() =&gt; new Bolt(sparks), capacity: 8);
    ///
    /// protected override void CollectAssets(AssetCollection assets) =&gt; _bolts.CollectAssets(assets);
    /// </code>
    /// </example>
    /// <param name="assets">The collection the owner's hook was handed.</param>
    public void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _forwarded |= !assets.IsProbe;
        if (_collecting)
        {
            return;
        }

        _collecting = true;
        try
        {
            // Indexed because a pooled entity can declare its own scene pool larger, which builds more.
            for (int index = 0; index < _built.Count; index++)
            {
                CollectTree(_built[index], assets);
            }
        }
        finally
        {
            _collecting = false;
        }
    }

    // Builds idle entities until the pool holds capacity. Never shrinks.
    internal void Reserve(int capacity)
    {
        _sizedCapacity = Math.Max(_sizedCapacity, capacity);

        // A taken entity returns to the idle stack, which then holds every entity at once without growing.
        _idle.EnsureCapacity(capacity);
        while (_built.Count < capacity)
        {
            _idle.Push(Build());
        }
    }

    bool IEntityPool.Shared => Shared;

    void IEntityPool.Return(Entity entity)
    {
        T pooled = (T)entity;
        if (pooled.Parent is not null)
        {
            pooled.Unparent();
        }

        pooled.IdleInPool = true;
        _idle.Push(pooled);
    }

    private T Build()
    {
        T entity = _create();

        if (entity.Pool is not null)
        {
            throw new InvalidOperationException(
                $"An EntityPool<{typeof(T).Name}>'s create delegate returned an entity already owned by a pool. Return a new instance from every call.");
        }

        entity.Pool = this;
        entity.IdleInPool = true;
        _built.Add(entity);
        return entity;
    }

    // Runs once, at the first take. A probe collection leaves the pools it reaches unforwarded.
    private void CheckForwarded()
    {
        if (_checkedForward)
        {
            return;
        }

        _checkedForward = true;
        if (_forwarded)
        {
            return;
        }

        AssetCollection probe = new() { IsProbe = true };
        CollectAssets(probe);
        if (!probe.HoldsPreloads)
        {
            return;
        }

        if (Shared)
        {
            Log.Info($"The scene's shared EntityPool<{typeof(T).Name}> was not declared before its first take. Declare it with assets.Pool<{typeof(T).Name}>(capacity) in the CollectAssets of what takes from it, and have that in the scene before it starts, to preload its assets");
        }
        else
        {
            Log.Info($"EntityPool<{typeof(T).Name}>'s assets were not collected before its first take. Forward the pool from its owner's CollectAssets, and have the owner in the scene before it starts, to preload them");
        }
    }

    // A scene holds an entity's descendants as entities of their own and collects each.
    private static void CollectTree(Entity entity, AssetCollection assets)
    {
        entity.CollectAssetPreloads(assets);
        foreach (Entity child in entity.Children)
        {
            CollectTree(child, assets);
        }
    }
}

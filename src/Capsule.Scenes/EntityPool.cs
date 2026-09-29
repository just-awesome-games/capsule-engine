using System.Diagnostics.CodeAnalysis;
using Capsule.Assets;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

// The link an Entity carries back to the pool that owns it. Non-generic so Entity, which knows
// nothing of T, can hold one.
internal interface IEntityPool
{
    void Return(Entity entity);
}

/// <summary>
/// Hands out <typeparamref name="T"/> entities built once and reused for the pool's life. The
/// engine returns an entity to this pool the moment its removal from a scene lands, or when the
/// scene stops.
/// </summary>
/// <remarks>
/// The game writes no release call, and the pooled class holds no pool reference. Idle entities sit
/// outside any scene. The pool's owner forwards it from its own <c>CollectAssets</c> through
/// <see cref="CollectAssets"/> to preload what they draw and play. The first take logs once at
/// <see cref="Log.Info"/> when no preload collection has reached the pool and its entities hold assets
/// to preload.
/// </remarks>
/// <example>
/// <code>
/// private EntityPool&lt;Bolt&gt; _bolts = new(() =&gt; new Bolt(sparks), capacity: 8);
///
/// protected override void CollectAssets(AssetCollection assets) =&gt; _bolts.CollectAssets(assets);
///
/// protected override void OnLateStep(in StepContext context)
/// {
///     if (!ShotThisStep) return;
///     Scene.Add(_bolts.Take().Fire(Muzzle.WorldPosition, _visual.Facing, _bolt));
/// }
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
    private readonly int _constructedCapacity;
    private bool _loggedGrowth;

    // Whether CollectAssets has run outside a probe, and whether the first take has checked it.
    private bool _forwarded;
    private bool _checkedForward;

    // Set while this pool collects. An entity that forwards the pool it lives in stops here.
    private bool _collecting;

    /// <summary>Builds <paramref name="capacity"/> entities now through <paramref name="create"/> and holds them idle.</summary>
    /// <param name="create">Builds one more entity, on demand. Every entity it ever returns is owned by this pool for life.</param>
    /// <param name="capacity">How many entities to build now. Positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="create"/> returned an entity already owned by a pool.</exception>
    public EntityPool(Func<T> create, int capacity)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        _create = create;
        _constructedCapacity = capacity;
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
    ///
    public T Take()
    {
        CheckForwarded();
        if (_idle.TryPop(out T? entity))
        {
            entity.IdleInPool = false;
            return entity;
        }

        if (!_loggedGrowth)
        {
            _loggedGrowth = true;
            Log.Debug($"{typeof(T).Name} pool grew past {_constructedCapacity}. Size the pool for its peak");
        }

        T built = Build();
        built.IdleInPool = false;
        return built;
    }

    /// <summary>Pops an idle entity, or returns false without building one.</summary>
    public bool TryTake([NotNullWhen(true)] out T? entity)
    {
        CheckForwarded();
        if (_idle.TryPop(out entity))
        {
            entity.IdleInPool = false;
            return true;
        }

        entity = null;
        return false;
    }

    /// <summary>
    /// Appends the assets of every entity this pool has built, as a scene collects them for the
    /// entities it holds.
    /// </summary>
    /// <remarks>
    /// Call it from the <c>CollectAssets</c> override of the entity or scene that declares the pool.
    /// A pool nothing forwards preloads nothing, and its entities' textures and sounds load on first
    /// use mid-play.
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
            foreach (T entity in _built)
            {
                CollectTree(entity, assets);
            }
        }
        finally
        {
            _collecting = false;
        }
    }

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
        if (probe.HoldsPreloads)
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

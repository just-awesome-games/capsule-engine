using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Tiles;

namespace Capsule.Scenes;

// A scene's shared entity pools and the preload collection that sizes them from what its hooks declare.
internal sealed class ScenePools
{
    private readonly Scene _scene;

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

    // Whether a preload collection has run. The scene runs one as it starts when no host did.
    internal bool PreloadsCollected { get; private set; }

    internal ScenePools(Scene scene)
    {
        _scene = scene;
    }

    // The shared pool of T, built with one entity when no declaration reached it.
    internal EntityPool<T> Get<T>()
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
    internal void Declare<T>(int capacity)
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

    // Runs every CollectAssets hook the scene reaches into assets and returns it. A collection that is not
    // a probe counts as the scene's preload and sizes the scene for every collected entity.
    internal AssetCollection Collect(AssetCollection assets)
    {
        if (!assets.IsProbe)
        {
            PreloadsCollected = true;
        }

        assets.GatheringScene = _scene;
        try
        {
            _scene.CollectAssets(assets);
            if (_scene.AuthoredAssets is { } authored)
            {
                assets.Add(authored);
            }

            foreach (Entity entity in _scene.Entities)
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

        return assets;
    }

    // Whether pool is target or grew from target through the declarations recorded so far. The recursion
    // ends because the graph has no cycle: Declare adds an edge only where this returned false.
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
        Scene scene = _scene;
        ScenePhysics physics = scene.Physics;
        int entities = scene.SlotCount;
        int reporters = physics.ContactReporterCount;
        int notifiers = scene.Visibility.Count;
        int colliders = physics.World.ColliderCount;
        int grids = physics.World.Grids.Length;
        int renderers = 0;
        foreach (Entity entity in _collected)
        {
            // The counts above hold what the scene has slotted and joined already. An entity joined to another
            // scene, from a pool both share, still joins this one. The draw order counts every renderer.
            bool joins = !entity.Joined || entity.SceneOrNull != scene;
            entities += entity.SceneOrNull != scene ? 1 : 0;
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

        scene.ReserveMembers(entities);
        physics.Reserve(colliders, grids, reporters);
        scene.Visibility.Reserve(notifiers);
        scene.Drawing.Reserve(renderers);
    }

    // One shared pool's declarations in a collection: the sum of those that are not ancestral, and the
    // largest that is.
    private readonly record struct PoolDeclarations(int Summed, int Ancestral);
}

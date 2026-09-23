using System.Diagnostics.CodeAnalysis;
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
/// The game writes no release call, and the pooled class holds no pool reference.
/// </remarks>
/// <example>
/// <code>
/// private EntityPool&lt;Bolt&gt; _bolts = new(() =&gt; new Bolt(sparks), capacity: 8);
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
    private readonly int _constructedCapacity;
    private bool _loggedGrowth;

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

        for (int index = 0; index < capacity; index++)
        {
            _idle.Push(Build());
        }
    }

    /// <summary>Entities this pool has ever built, idle or taken.</summary>
    public int Capacity { get; private set; }

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
        if (_idle.TryPop(out entity))
        {
            entity.IdleInPool = false;
            return true;
        }

        entity = null;
        return false;
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
        Capacity++;
        return entity;
    }
}

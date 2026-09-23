namespace Capsule.Physics;

/// <summary>Which layers a query or a mover may hit.</summary>
/// <remarks>
/// <see cref="CollisionWorld2D.CreateFilter(System.ReadOnlySpan{string})"/> builds a filter from
/// layer names.
/// <para>
/// A filter belongs to the world that interned its layers. Mixing layers or filters from two worlds
/// throws <see cref="ArgumentException"/>, as does a layer no world interned, such as the value a
/// failed <see cref="CollisionWorld2D.TryFindLayer"/> leaves behind. <see cref="None"/> and
/// <see cref="Everything"/> name no table, and every world accepts them.
/// </para>
/// </remarks>
public readonly struct CollisionFilter : IEquatable<CollisionFilter>
{
    private readonly ulong _mask;
    private readonly int _world;

    private CollisionFilter(int world, ulong mask)
    {
        _world = world;
        _mask = mask;
    }

    /// <summary>Matches nothing and belongs to no world. This is the default filter value.</summary>
    public static CollisionFilter None => default;

    /// <summary>Matches every layer of every world, including layers interned after this value was made.</summary>
    public static CollisionFilter Everything => new(0, ulong.MaxValue);

    /// <summary>Whether this filter matches no layer.</summary>
    public bool IsEmpty => _mask == 0;

    internal int World => _world;

    // The raw layer bits, for the broadphase to test against a node's mask.
    internal ulong Bits => _mask;

    /// <summary>A filter matching <paramref name="layer"/> and nothing else.</summary>
    /// <exception cref="ArgumentException">No world interned the layer.</exception>
    public static CollisionFilter Of(CollisionLayer layer) => new(Interned(layer, nameof(layer)), Bit(layer));

    /// <summary>A filter matching every layer in <paramref name="layers"/>.</summary>
    /// <exception cref="ArgumentException">A layer was interned by no world, or the layers come from different worlds.</exception>
    public static CollisionFilter Of(params ReadOnlySpan<CollisionLayer> layers)
    {
        int world = 0;
        ulong mask = 0;
        foreach (CollisionLayer layer in layers)
        {
            world = Shared(world, Interned(layer, nameof(layers)), nameof(layers));
            mask |= Bit(layer);
        }

        return new CollisionFilter(world, mask);
    }

    /// <summary>Whether this filter matches <paramref name="layer"/>.</summary>
    /// <exception cref="ArgumentException">No world interned the layer, or another world did.</exception>
    public bool Matches(CollisionLayer layer)
    {
        _ = Shared(_world, Interned(layer, nameof(layer)), nameof(layer));

        return (_mask & Bit(layer)) != 0;
    }

    // The unvalidated bit test, for per-cell and per-proxy loops where the layer already came from
    // this world's tables.
    internal bool Admits(CollisionLayer layer) => (_mask & Bit(layer)) != 0;

    /// <summary>This filter, also matching <paramref name="layer"/>.</summary>
    /// <exception cref="ArgumentException">No world interned the layer, or another world did.</exception>
    public CollisionFilter With(CollisionLayer layer) =>
        new(Shared(_world, Interned(layer, nameof(layer)), nameof(layer)), _mask | Bit(layer));

    /// <summary>This filter, no longer matching <paramref name="layer"/>.</summary>
    /// <exception cref="ArgumentException">No world interned the layer, or another world did.</exception>
    public CollisionFilter Without(CollisionLayer layer) =>
        new(Shared(_world, Interned(layer, nameof(layer)), nameof(layer)), _mask & ~Bit(layer));

    /// <summary>A filter matching what either matches.</summary>
    /// <exception cref="ArgumentException">The two filters belong to different worlds.</exception>
    public static CollisionFilter operator |(CollisionFilter left, CollisionFilter right) =>
        new(Shared(left._world, right._world, nameof(right)), left._mask | right._mask);

    /// <summary>A filter matching what both match.</summary>
    /// <exception cref="ArgumentException">The two filters belong to different worlds.</exception>
    public static CollisionFilter operator &(CollisionFilter left, CollisionFilter right) =>
        new(Shared(left._world, right._world, nameof(right)), left._mask & right._mask);

    /// <summary>Whether two filters match the same set of layers of the same world.</summary>
    public static bool operator ==(CollisionFilter left, CollisionFilter right) => left.Equals(right);

    /// <summary>Whether two filters match different sets of layers, or belong to different worlds.</summary>
    public static bool operator !=(CollisionFilter left, CollisionFilter right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(CollisionFilter other) => _world == other._world && _mask == other._mask;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CollisionFilter other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_world, _mask);

    // A layer's world, required to be non-zero. An unstamped layer is a zero index, so treating it
    // as world-agnostic would build a filter every world accepts as its own index-0 entry.
    private static int Interned(CollisionLayer layer, string parameterName) =>
        layer.World != 0
            ? layer.World
            : throw new ArgumentException("Layer was never interned. Intern the layer name on a collision world first.", parameterName);

    // The world two operands agree on. Zero is the world-agnostic value of None and Everything, and
    // it adopts whichever world it meets.
    private static int Shared(int left, int right, string parameterName)
    {
        if (left == 0)
        {
            return right;
        }

        if (right == 0 || left == right)
        {
            return left;
        }

        throw new ArgumentException("The two filters belong to different collision worlds.", parameterName);
    }

    private static ulong Bit(CollisionLayer layer) => 1UL << layer.Index;
}

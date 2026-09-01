namespace Capsule.Collision;

/// <summary>
/// Which layers a query or a mover may hit, held as one bit per <see cref="CollisionLayer"/>. Built
/// from names at setup — <see cref="CollisionWorld2D.Filter(System.ReadOnlySpan{string})"/> — and
/// read as a mask afterwards, so matching costs one bit test.
/// <para>
/// A filter built from layers belongs to the world that interned them, because a bit means nothing
/// without the table it indexes. Mixing two worlds' layers or filters, and testing a filter against
/// a layer from elsewhere, throw <see cref="ArgumentException"/> rather than aliasing one world's
/// names onto another's — as does any layer no world interned, such as the one a failed
/// <see cref="CollisionWorld2D.TryFindLayer"/> leaves behind.
/// </para>
/// <para>
/// <see cref="None"/> and <see cref="Everything"/> name no table, and they alone are accepted by
/// every world: agnosticism is a property of those two values, never of a layer.
/// </para>
/// </summary>
public readonly struct CollisionFilter : IEquatable<CollisionFilter>
{
    private readonly ulong _mask;
    private readonly int _world;

    private CollisionFilter(int world, ulong mask)
    {
        _world = world;
        _mask = mask;
    }

    /// <summary>Matches nothing; the value of a default filter, and belongs to no world.</summary>
    public static CollisionFilter None => default;

    /// <summary>
    /// Matches every layer of every world, including ones interned after this value was made.
    /// </summary>
    public static CollisionFilter Everything => new(0, ulong.MaxValue);

    /// <summary>Whether this filter matches no layer at all.</summary>
    public bool IsEmpty => _mask == 0;

    internal int World => _world;

    /// <summary>A filter matching exactly <paramref name="layer"/>.</summary>
    /// <exception cref="ArgumentException">No world interned the layer.</exception>
    public static CollisionFilter Of(CollisionLayer layer) => new(Interned(layer, nameof(layer)), Bit(layer));

    /// <summary>A filter matching every layer in <paramref name="layers"/>.</summary>
    /// <exception cref="ArgumentException">A layer was interned by no world, or by more than one between them.</exception>
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

    /// <summary>Whether <paramref name="layer"/> is one this filter matches.</summary>
    /// <exception cref="ArgumentException">No world interned the layer, or another world did.</exception>
    public bool Matches(CollisionLayer layer)
    {
        _ = Shared(_world, Interned(layer, nameof(layer)), nameof(layer));

        return (_mask & Bit(layer)) != 0;
    }

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

    /// <summary>A filter matching what either matches; the operator's named form.</summary>
    /// <exception cref="ArgumentException">The two filters belong to different worlds.</exception>
    public CollisionFilter Union(CollisionFilter other) => this | other;

    /// <summary>A filter matching what both match; the operator's named form.</summary>
    /// <exception cref="ArgumentException">The two filters belong to different worlds.</exception>
    public CollisionFilter Intersect(CollisionFilter other) => this & other;

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

    // A layer's world, insisting it has one. Agnosticism belongs to None and Everything, which name
    // no table; an unstamped layer is the zero value of a type whose whole content is a table index,
    // and treating it as agnostic would build a filter every world accepts and every world reads
    // as its own index-0 entry. The default out value of a failed lookup arrives exactly this way.
    private static int Interned(CollisionLayer layer, string parameterName) =>
        layer.World != 0
            ? layer.World
            : throw new ArgumentException(
                "No collision world interned that layer, so there is no table for a filter bit to mean anything in; intern the name first, or check the result of TryFindLayer before using it.",
                parameterName);

    // The world two operands agree on. Zero is the world-agnostic value that None and Everything
    // carry, so it takes on whichever world it meets rather than fighting it.
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

        throw new ArgumentException(
            "A collision filter's bits index one world's layer table; combining it with another world's would match whatever names happen to sit at the same indices.",
            parameterName);
    }

    private static ulong Bit(CollisionLayer layer) => 1UL << layer.Index;
}

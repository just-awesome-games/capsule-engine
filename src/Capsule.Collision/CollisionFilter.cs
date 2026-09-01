namespace Capsule.Collision;

/// <summary>
/// Which tags a query or a mover may hit, held as one bit per <see cref="CollisionTag"/>. Built
/// from names at setup — <see cref="CollisionWorld.Filter(System.ReadOnlySpan{string})"/> — and
/// read as a mask afterwards, so matching costs one bit test.
/// <para>
/// A filter built from tags belongs to the world that interned them, because a bit means nothing
/// without the table it indexes. Mixing two worlds' tags or filters, and testing a filter against
/// a tag from elsewhere, throw <see cref="ArgumentException"/> rather than aliasing one world's
/// names onto another's — as does any tag no world interned, such as the one a failed
/// <see cref="CollisionWorld.TryFindTag"/> leaves behind.
/// </para>
/// <para>
/// <see cref="None"/> and <see cref="Everything"/> name no table, and they alone are accepted by
/// every world: agnosticism is a property of those two values, never of a tag.
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
    /// Matches every tag of every world, including ones interned after this value was made.
    /// </summary>
    public static CollisionFilter Everything => new(0, ulong.MaxValue);

    /// <summary>Whether this filter matches no tag at all.</summary>
    public bool IsEmpty => _mask == 0;

    internal int World => _world;

    /// <summary>A filter matching exactly <paramref name="tag"/>.</summary>
    /// <exception cref="ArgumentException">No world interned the tag.</exception>
    public static CollisionFilter Of(CollisionTag tag) => new(Interned(tag, nameof(tag)), Bit(tag));

    /// <summary>A filter matching every tag in <paramref name="tags"/>.</summary>
    /// <exception cref="ArgumentException">A tag was interned by no world, or by more than one between them.</exception>
    public static CollisionFilter Of(params ReadOnlySpan<CollisionTag> tags)
    {
        int world = 0;
        ulong mask = 0;
        foreach (CollisionTag tag in tags)
        {
            world = Shared(world, Interned(tag, nameof(tags)), nameof(tags));
            mask |= Bit(tag);
        }

        return new CollisionFilter(world, mask);
    }

    /// <summary>Whether <paramref name="tag"/> is one this filter matches.</summary>
    /// <exception cref="ArgumentException">No world interned the tag, or another world did.</exception>
    public bool Matches(CollisionTag tag)
    {
        _ = Shared(_world, Interned(tag, nameof(tag)), nameof(tag));

        return (_mask & Bit(tag)) != 0;
    }

    /// <summary>This filter, also matching <paramref name="tag"/>.</summary>
    /// <exception cref="ArgumentException">No world interned the tag, or another world did.</exception>
    public CollisionFilter With(CollisionTag tag) =>
        new(Shared(_world, Interned(tag, nameof(tag)), nameof(tag)), _mask | Bit(tag));

    /// <summary>This filter, no longer matching <paramref name="tag"/>.</summary>
    /// <exception cref="ArgumentException">No world interned the tag, or another world did.</exception>
    public CollisionFilter Without(CollisionTag tag) =>
        new(Shared(_world, Interned(tag, nameof(tag)), nameof(tag)), _mask & ~Bit(tag));

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

    /// <summary>Whether two filters match the same set of tags of the same world.</summary>
    public static bool operator ==(CollisionFilter left, CollisionFilter right) => left.Equals(right);

    /// <summary>Whether two filters match different sets of tags, or belong to different worlds.</summary>
    public static bool operator !=(CollisionFilter left, CollisionFilter right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(CollisionFilter other) => _world == other._world && _mask == other._mask;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CollisionFilter other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_world, _mask);

    // A tag's world, insisting it has one. Agnosticism belongs to None and Everything, which name
    // no table; an unstamped tag is the zero value of a type whose whole content is a table index,
    // and treating it as agnostic would build a filter every world accepts and every world reads
    // as its own index-0 entry. The default out value of a failed lookup arrives exactly this way.
    private static int Interned(CollisionTag tag, string parameterName) =>
        tag.World != 0
            ? tag.World
            : throw new ArgumentException(
                "No collision world interned that tag, so there is no table for a filter bit to mean anything in; intern the name first, or check the result of TryFindTag before using it.",
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
            "A collision filter's bits index one world's tag table; combining it with another world's would match whatever names happen to sit at the same indices.",
            parameterName);
    }

    private static ulong Bit(CollisionTag tag) => 1UL << tag.Index;
}

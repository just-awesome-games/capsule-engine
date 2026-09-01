namespace Capsule.Collision;

/// <summary>
/// A collider's name interned by one <see cref="CollisionWorld2D"/>, held as the index it interned
/// to. Setup code speaks the string; results carry this, so no hot path compares text and every
/// query result stays unmanaged — a caller may keep its contact buffer on the stack.
/// <see cref="CollisionWorld2D.NameOf"/> reads the name back. A tag also carries the world that
/// interned it: two worlds' tags never compare equal even at the same index, and a world rejects a
/// tag it did not intern. The default value is no world's tag.
/// </summary>
public readonly struct CollisionTag : IEquatable<CollisionTag>
{
    internal CollisionTag(int world, int index)
    {
        World = world;
        Index = index;
    }

    /// <summary>The tag's position in its world's table, from 0 to 63.</summary>
    public int Index { get; }

    internal int World { get; }

    /// <summary>Whether two tags are the same entry of the same world's table.</summary>
    public static bool operator ==(CollisionTag left, CollisionTag right) => left.Equals(right);

    /// <summary>Whether two tags are different entries, or entries of different worlds.</summary>
    public static bool operator !=(CollisionTag left, CollisionTag right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(CollisionTag other) => World == other.World && Index == other.Index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CollisionTag other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(World, Index);
}

namespace Capsule.Collision;

/// <summary>
/// One named layer of one <see cref="CollisionWorld2D"/>, held as the index it interned to, so no
/// hot path compares text and query results stay unmanaged.
/// <see cref="CollisionWorld2D.NameOf"/> reads the name back. A layer carries the world that
/// interned it: two worlds' layers never compare equal even at the same index, and a world rejects
/// a layer it did not intern. The default value is no world's layer.
/// </summary>
public readonly struct CollisionLayer : IEquatable<CollisionLayer>
{
    internal CollisionLayer(int world, int index)
    {
        World = world;
        Index = index;
    }

    /// <summary>The layer's position in its world's table, from 0 to 63.</summary>
    public int Index { get; }

    internal int World { get; }

    /// <summary>Whether two layers are the same entry of the same world's table.</summary>
    public static bool operator ==(CollisionLayer left, CollisionLayer right) => left.Equals(right);

    /// <summary>Whether two layers are different entries, or entries of different worlds.</summary>
    public static bool operator !=(CollisionLayer left, CollisionLayer right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(CollisionLayer other) => World == other.World && Index == other.Index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CollisionLayer other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(World, Index);
}

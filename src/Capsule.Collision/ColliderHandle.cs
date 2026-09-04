namespace Capsule.Collision;

/// <summary>
/// Identifies one collider in one <see cref="CollisionWorld2D"/>. Handles are not reused: a slot
/// refilled after a removal hands out a handle that no longer equals the old one, so a stale
/// handle reads as absent. A handle carries the world that issued it, and every world API rejects
/// a foreign handle.
/// </summary>
public readonly struct ColliderHandle : IEquatable<ColliderHandle>
{
    internal ColliderHandle(int world, int index, int generation)
    {
        World = world;
        Index = index;
        Generation = generation;
    }

    /// <summary>The handle no collider ever has, and the one every world accepts as "nothing".</summary>
    public static ColliderHandle None => default;

    /// <summary>Whether this is <see cref="None"/> rather than a collider.</summary>
    public bool IsNone => Generation == 0;

    internal int World { get; }

    internal int Index { get; }

    internal int Generation { get; }

    /// <summary>Whether two handles name the same collider of the same world.</summary>
    public static bool operator ==(ColliderHandle left, ColliderHandle right) => left.Equals(right);

    /// <summary>Whether two handles name different colliders, or colliders of different worlds.</summary>
    public static bool operator !=(ColliderHandle left, ColliderHandle right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(ColliderHandle other) =>
        World == other.World && Index == other.Index && Generation == other.Generation;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ColliderHandle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(World, Index, Generation);
}

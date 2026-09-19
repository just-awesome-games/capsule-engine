namespace Capsule.Physics;

/// <summary>
/// Identifies a collider in a <see cref="CollisionWorld2D"/>. A handle carries the world that
/// issued it, and world APIs reject a foreign handle.
/// <para>
/// Handles are not reused. A slot refilled after a removal issues a different handle, and a stale
/// handle reads as absent.
/// </para>
/// </summary>
public readonly struct ColliderHandle : IEquatable<ColliderHandle>
{
    internal ColliderHandle(int world, int index, int generation)
    {
        World = world;
        Index = index;
        Generation = generation;
    }

    /// <summary>The handle no collider has. Every world accepts it as "nothing".</summary>
    public static ColliderHandle None => default;

    /// <summary>Whether this handle is <see cref="None"/>.</summary>
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

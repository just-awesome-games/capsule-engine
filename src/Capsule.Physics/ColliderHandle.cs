namespace Capsule.Physics;

/// <summary>Identifies a collider in a <see cref="CollisionWorld2D"/>.</summary>
/// <remarks>
/// A handle carries the world that issued it, and world APIs reject a foreign handle.
/// <para>
/// Handles are not reused. A slot refilled after a removal issues a different handle, and a stale
/// handle reads as absent.
/// </para>
/// </remarks>
public readonly record struct ColliderHandle
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
}

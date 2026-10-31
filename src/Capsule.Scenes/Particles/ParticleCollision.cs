using Capsule.Physics;

namespace Capsule.Particles;

/// <summary>
/// What a particle does when its move in a step meets something on a layer the mask names: it bounces off
/// or dies there. The default collides with nothing.
/// </summary>
/// <remarks>
/// Each live particle casts one ray a step along its move, against tile grids and colliders alike. A layer
/// with no collider on it costs nothing beyond its grids' cells. A one-way surface stops only what comes
/// from its solid side. A moving collider neither carries nor pushes a particle.
/// </remarks>
public readonly record struct ParticleCollision
{
    internal CollisionMask? Mask { get; }

    internal bool Kills { get; }

    internal float Bounciness { get; }

    internal float Friction { get; }

    private ParticleCollision(CollisionMask with, bool kills, float bounce, float friction)
    {
        ArgumentNullException.ThrowIfNull(with);

        Mask = with;
        Kills = kills;
        Bounciness = bounce;
        Friction = friction;
    }

    /// <summary>Particles bounce off what <paramref name="with"/> names and fly on.</summary>
    /// <param name="with">The layers that stop a particle.</param>
    /// <param name="bounce">The fraction of its speed into the surface a particle keeps, from 0 to 1. Zero lands it there.</param>
    /// <param name="friction">The fraction of its speed along the surface a particle loses at each touch, from 0 to 1.</param>
    public static ParticleCollision Bounce(CollisionMask with, float bounce, float friction = 0f) =>
        new(with, false, Guard.InUnit(bounce, nameof(bounce)), Guard.InUnit(friction, nameof(friction)));

    /// <summary>Particles die where they meet what <paramref name="with"/> names, before they draw there.</summary>
    /// <param name="with">The layers that end a particle.</param>
    public static ParticleCollision Kill(CollisionMask with) => new(with, true, 0f, 0f);
}

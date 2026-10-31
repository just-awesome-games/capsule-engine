using System.Numerics;

namespace Capsule.Particles;

/// <summary>
/// A swirling flow field that particles drift through. It never gathers them into clumps or thins them out.
/// </summary>
/// <remarks>
/// The push moves a particle on top of its own velocity and never changes that velocity. It never builds up,
/// and it stays within <see cref="Strength"/> with no damping. Neighbouring particles swirl together.
/// Each emitter has a field of its own, which draws nothing from its random stream.
/// </remarks>
/// <example>
/// Motes that wander as they rise, through swirls about 32 units across:
/// <code>
/// Noise = new ParticleNoise(strength: 12f, size: 32f),
/// </code>
/// </example>
public readonly record struct ParticleNoise
{
    /// <summary>The most the field pushes a particle along either axis, in units per second. Zero switches it off.</summary>
    public float Strength { get; }

    /// <summary>How wide one swirl is, in world units.</summary>
    public float Size { get; }

    /// <summary>
    /// The velocity the whole field drifts at, in units per second along world axes, or the entity's own axes under a
    /// local emitter, whose field rides the entity. Zero by default, which holds it still.
    /// </summary>
    public Vector2 Scroll
    {
        get;
        init => field = Guard.Finite(value, nameof(value));
    }

    /// <summary>Creates a still field.</summary>
    /// <param name="strength">The most the field pushes along either axis, in units per second.</param>
    /// <param name="size">How wide one swirl is, in world units.</param>
    public ParticleNoise(float strength, float size)
    {
        Strength = Guard.NonNegative(strength, nameof(strength));
        Size = Guard.Positive(size, nameof(size));
    }
}

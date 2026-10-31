namespace Capsule.Particles;

/// <summary>A burst another emitter spawns where a particle dies. The default spawns nothing.</summary>
public readonly record struct SubEmission
{
    /// <summary>The emitter that spawns the burst, null for none.</summary>
    public ParticleEmitter? Into { get; }

    /// <summary>How many particles each death spawns.</summary>
    public int Count { get; }

    /// <summary>Creates a burst of <paramref name="count"/> particles from <paramref name="into"/>.</summary>
    /// <param name="into">The emitter that spawns the burst, with its own shape, launch and look.</param>
    /// <param name="count">How many particles each death spawns, at least one.</param>
    public SubEmission(ParticleEmitter into, int count)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        Into = into;
        Count = count;
    }
}

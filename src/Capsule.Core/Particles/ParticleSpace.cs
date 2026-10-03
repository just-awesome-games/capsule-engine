namespace Capsule.Particles;

/// <summary>Where an emitter keeps its live particles.</summary>
public enum ParticleSpace
{
    /// <summary>The default, in which particles stay where they spawn as the entity moves on.</summary>
    World,

    /// <summary>Particles ride the entity, turning and mirroring with it.</summary>
    Local,
}

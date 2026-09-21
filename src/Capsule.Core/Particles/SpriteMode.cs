namespace Capsule.Particles;

/// <summary>How a particle with more than one frame picks which to draw.</summary>
public enum SpriteMode
{
    /// <summary>One frame drawn at spawn, for the particle's life. No draw with one frame.</summary>
    RandomAtSpawn,

    /// <summary>The frame by age fraction, landing on the last frame at the end of the particle's life.</summary>
    OverLife,
}

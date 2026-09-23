namespace Capsule.Particles;

/// <summary>How a particle with more than one frame picks which to draw.</summary>
public enum SpriteMode
{
    /// <summary>A frame picked at random at spawn and kept for the particle's life. A single-frame particle consumes no random draw.</summary>
    RandomAtSpawn,

    /// <summary>The frame by age fraction, landing on the last frame at the end of the particle's life.</summary>
    OverLife,
}

namespace Capsule.Particles;

/// <summary>How a particle with more than one frame picks which to draw.</summary>
public enum SpriteMode
{
    /// <summary>A frame picked at random at spawn and kept for the particle's life. A single-frame particle consumes no random draw.</summary>
    RandomAtSpawn,

    /// <summary>The frame by age fraction, starting on the first frame with the frames sharing the particle's life as evenly as whole steps allow.</summary>
    /// <remarks>Two frames over an 8-step life hold 4 steps each.</remarks>
    OverLife,
}

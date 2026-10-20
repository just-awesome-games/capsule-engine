namespace Capsule.Particles;

/// <summary>How a particle plays its emitter's clip.</summary>
public enum FrameMode
{
    /// <summary>Each frame held for its authored steps from the particle's spawn.</summary>
    /// <remarks>A looping clip wraps, and a non-looping clip ends the particle after its last frame if its lifetime has not already.</remarks>
    Clip,

    /// <summary>The clip's steps stretched or squeezed to fill the particle's life, played once.</summary>
    /// <remarks>Frames held for 1 and 3 steps over an 8-step life hold 2 and 6.</remarks>
    OverLife,

    /// <summary>A frame picked at random at spawn and kept for the particle's life.</summary>
    /// <remarks>A one-frame clip consumes no random draw.</remarks>
    RandomAtSpawn,
}

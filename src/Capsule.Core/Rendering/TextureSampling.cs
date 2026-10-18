namespace Capsule.Rendering;

/// <summary>How textures are filtered while rasterising world render intent.</summary>
public enum TextureSampling
{
    /// <summary>Interpolates neighbouring texels.</summary>
    Linear,

    /// <summary>Uses the nearest texel.</summary>
    /// <remarks>
    /// As the run's sampling it snaps each sprite to the render surface's pixel grid. An unturned sprite,
    /// or one turned by whole quarter turns, snaps all four edges, and abutting tiles share their edges
    /// at any zoom.
    /// </remarks>
    Point,
}

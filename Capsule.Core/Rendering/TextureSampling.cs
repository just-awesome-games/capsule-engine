namespace Capsule.Rendering;

/// <summary>How textures are filtered while rasterising world render intent.</summary>
public enum TextureSampling
{
    /// <summary>Interpolates neighbouring texels.</summary>
    Linear,

    /// <summary>Uses the nearest texel.</summary>
    Point,
}

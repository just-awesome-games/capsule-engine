namespace Capsule.Rendering;

/// <summary>
/// A rectangle of texels within a texture, measured from its top-left corner. Y-down, like the
/// world.
/// </summary>
/// <param name="X">The region's left edge, in texels from the texture's left.</param>
/// <param name="Y">The region's top edge, in texels from the texture's top.</param>
/// <param name="Width">The region's width in texels.</param>
/// <param name="Height">The region's height in texels.</param>
public readonly record struct TextureRegion(int X, int Y, int Width, int Height);

namespace Capsule.Rendering;

/// <summary>A rectangle of texels within a texture, measured from its top-left corner and Y-down like the world.</summary>
public readonly record struct TextureRegion(int X, int Y, int Width, int Height);

namespace Capsule.Assets;

/// <summary>Pure data naming <c>assets/textures/{Name}{Extension}</c> beside the executable.</summary>
public readonly record struct TextureHandle(string Name, string Extension);

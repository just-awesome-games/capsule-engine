namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/textures/{Name}{Extension}</c> beside the executable. <c>Name</c> is
/// the source's path under the <c>textures</c> root, forward slashes and no extension.
/// </summary>
public readonly record struct TextureHandle(string Name, string Extension);

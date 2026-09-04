namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/textures/{Name}{Extension}</c> beside the executable. <c>Name</c> is the
/// source's path under the <c>textures</c> root with forward slashes and no extension, so a nested
/// asset keeps its directories: <c>enemies/bat</c> ships at <c>assets/textures/enemies/bat.png</c>.
/// </summary>
public readonly record struct TextureHandle(string Name, string Extension);

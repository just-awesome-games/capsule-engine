namespace Capsule.Assets;

/// <summary>
/// A texture the game ships, named by the file stem it ships under. A handle is data and resolves
/// nothing: what it names lives at <c>Assets/textures</c> beside the executable, and reading those
/// bytes is the host's. Game logic therefore names a texture without touching a path.
/// </summary>
public readonly record struct TextureHandle(string Name);

namespace Capsule.Assets;

/// <summary>
/// A font the game ships, named by the file stem it ships under. A handle is data and resolves
/// nothing: what it names lives at <c>Assets/fonts</c> beside the executable, and reading those
/// bytes is the host's. Game logic therefore names a font without touching a path.
/// </summary>
public readonly record struct FontHandle(string Name);

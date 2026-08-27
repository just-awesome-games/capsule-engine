namespace Capsule.Assets;

/// <summary>
/// A sound the game ships, named by the file stem it ships under. A handle is data and resolves
/// nothing: what it names lives at <c>Assets/audio</c> beside the executable, and reading those
/// bytes is the host's. Game logic therefore names a sound without touching a path.
/// </summary>
public readonly record struct AudioHandle(string Name);

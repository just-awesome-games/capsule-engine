namespace Capsule.Assets;

/// <summary>
/// A font the game ships, as the stem and the extension of the file it ships under. A handle is
/// data and resolves nothing, but it is sufficient on its own to locate what it names: that file is
/// at <c>assets/fonts/{Name}{Extension}</c> beside the executable, so nothing downstream probes for
/// an extension and reading the bytes is the host's. Game logic therefore names a font without
/// touching a path.
/// </summary>
public readonly record struct FontHandle(string Name, string Extension);

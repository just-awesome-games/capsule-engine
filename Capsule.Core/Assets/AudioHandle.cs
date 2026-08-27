namespace Capsule.Assets;

/// <summary>
/// A sound the game ships, as the stem and the extension of the file it ships under. A handle is
/// data and resolves nothing, but it is sufficient on its own to locate what it names: that file is
/// at <c>assets/audio/{Name}{Extension}</c> beside the executable, so nothing downstream probes for
/// an extension and reading the bytes is the host's. Game logic therefore names a sound without
/// touching a path.
/// </summary>
public readonly record struct AudioHandle(string Name, string Extension);

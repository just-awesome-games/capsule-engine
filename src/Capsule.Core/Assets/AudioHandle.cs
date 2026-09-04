namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/audio/{Name}{Extension}</c> beside the executable. <c>Name</c> is the
/// source's path under the <c>audio</c> root, forward slashes and no extension.
/// </summary>
public readonly record struct AudioHandle(string Name, string Extension);

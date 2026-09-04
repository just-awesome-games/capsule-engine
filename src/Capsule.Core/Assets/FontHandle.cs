namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/fonts/{Name}{Extension}</c> beside the executable. <c>Name</c> is the
/// source's path under the <c>fonts</c> root, forward slashes and no extension.
/// </summary>
public readonly record struct FontHandle(string Name, string Extension);

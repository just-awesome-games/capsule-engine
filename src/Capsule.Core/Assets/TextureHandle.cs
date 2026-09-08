namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/textures/{Name}{Extension}</c> beside the executable. <c>Name</c> is
/// the source's path under the <c>textures</c> root: one or more forward-slash-separated portable
/// file-name segments, none empty, <c>.</c>, or <c>..</c>, and no extension. <c>Extension</c> begins
/// with one dot and contains no other dot or separator. Runtime loading rejects a handle that
/// does not meet this contract before accessing the file system.
/// </summary>
public readonly record struct TextureHandle(string Name, string Extension);

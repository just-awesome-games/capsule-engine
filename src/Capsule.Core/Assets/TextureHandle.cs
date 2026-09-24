namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/{Name}{Extension}</c> beside the executable.
/// </summary>
/// <remarks>
/// <c>Name</c> is the source's path under <c>Assets/</c>, forward slashes and no extension, and
/// <c>Extension</c> begins with one dot and contains no other dot or separator. Runtime loading
/// rejects a handle that breaks this contract before touching the file system. A handle the build
/// packed onto an atlas page is served from that page.
/// </remarks>
public readonly record struct TextureHandle(string Name, string Extension)
{
    // Whether the host owns this texture instead of a shipped file. Not positional, because no handle a
    // game writes is one. Equality still covers it, as it covers every field of a record struct.
    internal bool IsEngineOwned { get; private init; }

    /// <summary>
    /// One opaque white texel the host holds, for flat colour. It names no file, and an
    /// <see cref="AssetCollection"/> ignores it.
    /// </summary>
    public static TextureHandle White => new("white", ".engine") { IsEngineOwned = true };

    /// <summary>
    /// The engine's radial light falloff, which <see cref="Rendering.Sprite.Light"/> draws. It names no
    /// file, and an <see cref="AssetCollection"/> ignores it.
    /// </summary>
    public static TextureHandle Light => new("light", ".engine") { IsEngineOwned = true };

    internal static TextureHandle DefaultFontPage =>
        new("default-font", ".engine") { IsEngineOwned = true };
}

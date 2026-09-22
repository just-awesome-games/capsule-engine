using System.ComponentModel;

namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/textures/{Name}{Extension}</c> beside the executable, or
/// <c>assets/fonts/{Name}{Extension}</c> when the handle names a bitmap font's page. <c>Name</c> is
/// the source's path under that root, forward slashes and no extension, and <c>Extension</c> begins
/// with one dot and contains no other dot or separator. Runtime loading rejects a handle that breaks
/// this contract before touching the file system. A handle the build packed onto an atlas page is
/// served from that page.
/// </summary>
public readonly record struct TextureHandle(string Name, string Extension)
{
    // Not positional, because every handle a game writes is a texture and only generated code names the
    // other root. Equality still covers it, as it covers every field of a record struct.
    internal TextureDomain Domain { get; private init; }

    /// <summary>
    /// One opaque white texel the host holds. Flat colour is drawn from it. A filled rect is an
    /// ordinary tinted sprite. It names no file and an <see cref="AssetCollection"/> ignores it.
    /// </summary>
    public static TextureHandle White => new("white", ".engine") { Domain = TextureDomain.Engine };

    /// <summary>The engine's radial light falloff. Drawn from it, an ordinary sprite is a light of any other shape. Names no file and an <see cref="AssetCollection"/> ignores it.</summary>
    public static TextureHandle Light => new("light", ".engine") { Domain = TextureDomain.Engine };

    internal static TextureHandle DefaultFontPage =>
        new("default-font", ".engine") { Domain = TextureDomain.Engine };

    // Whether the host owns this texture instead of a file under a shipped root.
    internal bool IsEngineOwned => Domain == TextureDomain.Engine;

    /// <summary>A bitmap font page, which ships under <c>assets/fonts/</c>. Called by generated code.</summary>
    /// <param name="name">The page's path under the fonts root, forward slashes and no extension.</param>
    /// <param name="extension">The page's extension, leading dot included.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TextureHandle FontPage(string name, string extension) =>
        new(name, extension) { Domain = TextureDomain.Fonts };
}

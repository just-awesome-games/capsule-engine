using System.ComponentModel;

namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/textures/{Name}{Extension}</c> beside the executable — or
/// <c>assets/fonts/{Name}{Extension}</c> when the handle names a bitmap font's page, which ships
/// beside the font it was cut for. <c>Name</c> is the source's path under that root: one or more
/// forward-slash-separated portable file-name segments, none empty, <c>.</c>, or <c>..</c>, and no
/// extension. <c>Extension</c> begins with one dot and contains no other dot or separator. Runtime
/// loading rejects a handle that does not meet this contract before accessing the file system. Two
/// handles of one name resolving under different roots are two textures.
/// </summary>
public readonly record struct TextureHandle(string Name, string Extension)
{
    // Not positional: every handle a game writes is a texture, and only generated code names the
    // other root. Equality covers it, as it covers every other field of a record struct.
    internal TextureDomain Domain { get; private init; }

    /// <summary>
    /// A bitmap font page, which ships under <c>assets/fonts/</c>. Called by generated code; a game
    /// reaches a page through the <c>BitmapFont</c> that carries it.
    /// </summary>
    /// <param name="name">The page's path under the fonts root, forward slashes and no extension.</param>
    /// <param name="extension">The page's extension, leading dot included.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TextureHandle FontPage(string name, string extension) =>
        new(name, extension) { Domain = TextureDomain.Fonts };
}

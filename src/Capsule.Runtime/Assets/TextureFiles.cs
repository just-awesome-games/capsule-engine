using Capsule.Assets;

namespace Capsule.Runtime.Assets;

// Where a texture handle's file is. Separate from the store so the resolution and its failure are
// testable without a graphics device.
internal static class TextureFiles
{
    private static readonly AssetFiles Textures = new("textures", "Texture", "handle");

    // A bitmap font's pages ship beside the font they were cut for, so the same handle shape
    // resolves under a second root.
    private static readonly AssetFiles Fonts = new("fonts", "Font page", "handle");

    internal static string RelativePathOf(in TextureHandle handle) =>
        Files(handle).RelativePathOf(handle.Name, handle.Extension);

    internal static string Locate(string baseDirectory, in TextureHandle handle) =>
        Files(handle).Locate(baseDirectory, handle.Name, handle.Extension);

    private static AssetFiles Files(in TextureHandle handle) =>
        handle.Domain == TextureDomain.Fonts ? Fonts : Textures;
}

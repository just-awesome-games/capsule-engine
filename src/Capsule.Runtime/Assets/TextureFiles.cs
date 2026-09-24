using Capsule.Assets;

namespace Capsule.Runtime.Assets;

// Where a texture handle's file is. Separate from the store so resolution and its failure are testable
// without a graphics device.
internal static class TextureFiles
{
    private static readonly AssetFiles Textures = new("Texture", "handle");

    internal static string RelativePathOf(in TextureHandle handle) =>
        Textures.RelativePathOf(handle.Name, handle.Extension);

    internal static Stream Open(HostPlatform platform, in TextureHandle handle) =>
        Textures.Open(platform, handle.Name, handle.Extension);
}

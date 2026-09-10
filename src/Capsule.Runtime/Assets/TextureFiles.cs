using Capsule.Assets;

namespace Capsule.Runtime.Assets;

// Where a texture handle's file is. Separate from the store so the resolution and its failure are
// testable without a graphics device.
internal static class TextureFiles
{
    private static readonly AssetFiles Files = new("textures", "Texture", "handle");

    internal static string RelativePathOf(in TextureHandle handle) =>
        Files.RelativePathOf(handle.Name, handle.Extension);

    internal static string Locate(string baseDirectory, in TextureHandle handle) =>
        Files.Locate(baseDirectory, handle.Name, handle.Extension);
}

using Capsule.Assets;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Assets;

// A resolved handle: the texture to sample and where the handle's texel (0, 0) is on it. Offsets are
// zero for a texture served from its own file, and the page's offset for one the build packed.
internal readonly record struct TextureSlice(Texture2D Texture, int OffsetX, int OffsetY);

// The textures owned by the current scene, preloaded where declared and otherwise loaded on first
// draw. Residency is per file. A packed handle's file is its page, which loads once for any member and
// stays while any member of the incoming scene wants it.
internal sealed class TextureStore : IDisposable
{
    private readonly SceneAssetStore<TextureHandle, Texture2D> _textures;

    private readonly AtlasMap _atlases;

    internal TextureStore(GraphicsDevice device, HostPlatform platform)
    {
        _atlases = AtlasMap.Load(platform);
        _textures = new(Load);

        Texture2D Load(TextureHandle handle)
        {
            // The batch blends premultiplied, and a straight-alpha texture would fringe dark along
            // every soft edge. A packed page ships straight like any other file.
            using Stream file = TextureFiles.Open(platform, handle);
            return Texture2D.FromStream(device, file, DefaultColorProcessors.PremultiplyAlpha);
        }
    }

    // Missing preloads are decoded before the prior scene's textures are released.
    internal void ChangeScene(AssetCollection preloads, Action prepareRemainingAssets) =>
        _textures.ChangeScene(_atlases.Residency(preloads.Textures), prepareRemainingAssets);

    // Loads on first use when the scene did not preload the handle.
    internal TextureSlice Get(in TextureHandle handle) =>
        _atlases.TryGet(handle, out AtlasSlot slot)
            ? new TextureSlice(_textures.Get(slot.Page), slot.X, slot.Y)
            : new TextureSlice(_textures.Get(handle), 0, 0);

    public void Dispose() => _textures.Dispose();
}

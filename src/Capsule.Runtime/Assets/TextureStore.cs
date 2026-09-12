using Capsule.Assets;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Assets;

// The textures owned by the current scene, preloaded where declared and otherwise loaded on first
// draw.
internal sealed class TextureStore : IDisposable
{
    private readonly SceneAssetStore<TextureHandle, Texture2D> _textures;

    internal TextureStore(GraphicsDevice device)
    {
        _textures = new(Load);

        Texture2D Load(TextureHandle handle)
        {
            string path = TextureFiles.Locate(AppContext.BaseDirectory, handle);

            // The batch blends premultiplied, so a straight-alpha atlas would fringe dark along
            // every soft edge.
            using FileStream file = File.OpenRead(path);
            return Texture2D.FromStream(device, file, DefaultColorProcessors.PremultiplyAlpha);
        }
    }

    // Missing preloads are decoded before the prior scene's textures are released.
    internal void ChangeScene(AssetCollection preloads, Action prepareRemainingAssets) =>
        _textures.ChangeScene(preloads.Textures, prepareRemainingAssets);

    // Loads on first use when the scene did not preload the handle.
    internal Texture2D Get(in TextureHandle handle) => _textures.Get(handle);

    public void Dispose() => _textures.Dispose();
}

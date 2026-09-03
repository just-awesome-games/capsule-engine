using Capsule.Assets;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

/// <summary>
/// The textures resident on the device, keyed by handle. A set is loaded at once and disposed at
/// once; nothing is loaded on demand, so a draw either finds its texture or is a wiring fault.
/// </summary>
internal sealed class TextureStore : IDisposable
{
    private readonly Dictionary<TextureHandle, Texture2D> _textures;

    /// <summary>
    /// Decodes each distinct handle's file once, or leaves nothing on the device: a set is whole or
    /// it is not there, and a caller holding no store has no way to release half of one.
    /// </summary>
    /// <exception cref="FileNotFoundException">A handle's file is not beside the executable.</exception>
    internal TextureStore(GraphicsDevice device, IReadOnlyList<TextureHandle> handles)
    {
        // Located ahead of decoding, so a missing file costs no device memory at all.
        (TextureHandle Handle, string Path)[] resolved = TextureFiles.Resolve(AppContext.BaseDirectory, handles);
        _textures = new Dictionary<TextureHandle, Texture2D>(resolved.Length);

        try
        {
            foreach ((TextureHandle handle, string path) in resolved)
            {
                // The batch blends premultiplied and the tint path converts to premultiplied, so a
                // straight-alpha atlas would fringe dark along every soft edge.
                using FileStream file = File.OpenRead(path);
                _textures[handle] = Texture2D.FromStream(device, file, DefaultColorProcessors.PremultiplyAlpha);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The texture a handle names.</summary>
    /// <exception cref="InvalidOperationException">The set does not hold it.</exception>
    internal Texture2D Get(in TextureHandle handle) =>
        _textures.TryGetValue(handle, out Texture2D? texture)
            ? texture
            : throw new InvalidOperationException(
                $"Nothing draws texture '{handle.Name}': the loaded set holds no such handle, and the build ships a texture at '{TextureFiles.RelativePathOf(handle)}' only when its source sits under asset-sources/textures.");

    public void Dispose()
    {
        foreach (Texture2D texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
    }
}

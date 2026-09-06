using Capsule.Assets;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

// The textures resident on the device. The current scene's set decides what is here; nothing is
// loaded on demand from the frame path.
internal sealed class TextureStore : IDisposable
{
    private readonly ResidentTextureStore<Texture2D> _textures;

    internal TextureStore(GraphicsDevice device)
    {
        _textures = new(path =>
        {
            // The batch blends premultiplied, so a straight-alpha atlas would fringe dark along
            // every soft edge.
            using FileStream file = File.OpenRead(path);
            return Texture2D.FromStream(device, file, DefaultColorProcessors.PremultiplyAlpha);
        });
    }

    // Throws FileNotFoundException when an added handle's file is not beside the executable.
    internal void Change(string scene, IReadOnlyList<TextureHandle> load, IReadOnlyList<TextureHandle> release)
    {
        (TextureHandle Handle, string Path)[] resolved = TextureFiles.Resolve(AppContext.BaseDirectory, load);
        _textures.Change(scene, resolved, release);
    }

    // Throws InvalidOperationException when the current scene's set does not hold the handle.
    internal Texture2D Get(in TextureHandle handle) => _textures.Get(handle);

    public void Dispose() => _textures.Dispose();
}

// Stages every decode before committing a scene exchange. Generic only to keep device-free failure
// coverage over the ownership boundary; production closes it over Texture2D.
internal sealed class ResidentTextureStore<TTexture>(Func<string, TTexture> decode) : IDisposable
    where TTexture : class, IDisposable
{
    private readonly Dictionary<TextureHandle, TTexture> _textures = [];
    private string _scene = "the game";

    internal void Change(
        string scene,
        IReadOnlyList<(TextureHandle Handle, string Path)> load,
        IReadOnlyList<TextureHandle> release)
    {
        // Additions decode before releases are disposed, so device memory peaks at both sets at once.
        _textures.EnsureCapacity(_textures.Count + load.Count);
        List<(TextureHandle Handle, TTexture Texture)> staged = new(load.Count);

        try
        {
            foreach ((TextureHandle handle, string path) in load)
            {
                staged.Add((handle, decode(path)));
            }
        }
        catch
        {
            foreach ((TextureHandle _, TTexture texture) in staged)
            {
                texture.Dispose();
            }

            throw;
        }

        foreach (TextureHandle handle in release)
        {
            if (_textures.Remove(handle, out TTexture? dropped))
            {
                dropped.Dispose();
            }
        }

        foreach ((TextureHandle handle, TTexture texture) in staged)
        {
            _textures.Add(handle, texture);
        }

        _scene = scene;
    }

    internal TTexture Get(in TextureHandle handle) =>
        _textures.TryGetValue(handle, out TTexture? texture)
            ? texture
            : throw new InvalidOperationException(SceneResidency.NotResident(_scene, handle));

    public void Dispose()
    {
        foreach (TTexture texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
    }
}

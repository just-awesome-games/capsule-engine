using Capsule.Assets;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

// The textures resident on the device, keyed by handle. The current scene's set decides what is
// here; nothing is loaded on demand from the frame path.
internal sealed class TextureStore(GraphicsDevice device) : IDisposable
{
    private readonly Dictionary<TextureHandle, Texture2D> _textures = [];

    // Whose set is resident, so a draw naming a handle it does not hold can say where the wiring
    // broke. The window title until a scene has one, which is the deviceless host's case.
    private string _scene = "the game";

    // Decodes each added handle's file once and disposes each dropped one's texture, or leaves the
    // device exactly as it was. Throws FileNotFoundException when an added handle's file is not
    // beside the executable.
    internal void Change(string scene, IReadOnlyList<TextureHandle> load, IReadOnlyList<TextureHandle> release)
    {
        // Located ahead of every release, so a missing file costs neither device memory nor the
        // set the last scene was drawing.
        (TextureHandle Handle, string Path)[] resolved = TextureFiles.Resolve(AppContext.BaseDirectory, load);

        foreach (TextureHandle handle in release)
        {
            if (_textures.Remove(handle, out Texture2D? dropped))
            {
                dropped.Dispose();
            }
        }

        _scene = scene;

        try
        {
            foreach ((TextureHandle handle, string path) in resolved)
            {
                // The batch blends premultiplied, so a straight-alpha atlas would fringe dark
                // along every soft edge.
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

    // Throws InvalidOperationException when the current scene's set does not hold the handle.
    internal Texture2D Get(in TextureHandle handle) =>
        _textures.TryGetValue(handle, out Texture2D? texture)
            ? texture
            : throw new InvalidOperationException(SceneResidency.NotResident(_scene, handle));

    public void Dispose()
    {
        foreach (Texture2D texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
    }
}

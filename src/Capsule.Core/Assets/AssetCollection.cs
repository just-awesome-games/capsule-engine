using Capsule.Audio;
using Capsule.Rendering;

namespace Capsule.Assets;

/// <summary>
/// Assets an object graph asks the host to preload, kept once each in first-declaration order.
/// Collecting names does no device or file-system work.
/// </summary>
public sealed class AssetCollection
{
    private readonly List<TextureHandle> _textures = [];
    private readonly HashSet<TextureHandle> _textureSet = [];
    private readonly List<AudioClip> _clips = [];
    private readonly HashSet<AudioClip> _clipSet = [];
    private readonly List<Shader> _shaders = [];
    private readonly HashSet<Shader> _shaderSet = [];

    /// <summary>
    /// Adds one texture unless it was already declared. The engine's own textures, the white texel and
    /// the default font's page, belong to the host and are ignored here.
    /// </summary>
    public void Add(TextureHandle texture)
    {
        if (texture.IsEngineOwned)
        {
            return;
        }

        if (_textureSet.Add(texture))
        {
            _textures.Add(texture);
        }
    }

    /// <summary>Adds textures in order, ignoring any already declared.</summary>
    public void Add(ReadOnlySpan<TextureHandle> textures)
    {
        foreach (TextureHandle texture in textures)
        {
            Add(texture);
        }
    }

    /// <summary>
    /// Adds one clip unless it was already declared. Declaring a clip the host streams preloads no
    /// samples.
    /// </summary>
    public void Add(AudioClip clip)
    {
        if (_clipSet.Add(clip))
        {
            _clips.Add(clip);
        }
    }

    /// <summary>Adds clips in order, ignoring any already declared.</summary>
    public void Add(ReadOnlySpan<AudioClip> clips)
    {
        foreach (AudioClip clip in clips)
        {
            Add(clip);
        }
    }

    // A renderer's material: its shader, and every texture set on it. The scene collects this for each
    // renderer it holds, so no renderer declares its own.
    internal void Add(Material material)
    {
        if (_shaderSet.Add(material.Shader))
        {
            _shaders.Add(material.Shader);
        }

        for (int i = 0; i < material.Shader.Parameters.Length; i++)
        {
            if (material.TryGetTexture(i, out TextureHandle texture) && texture.Name is not null)
            {
                Add(texture);
            }
        }
    }

    internal IReadOnlyList<TextureHandle> Textures => _textures;

    internal IReadOnlyList<Shader> Shaders => _shaders;

    internal IReadOnlyList<AudioClip> Clips => _clips;
}

using Capsule.Audio;

namespace Capsule.Assets;

/// <summary>
/// Assets an object graph asks the host to preload, kept once in first-declaration order.
/// Pure names only; collecting them performs no device or file-system work.
/// </summary>
public sealed class AssetCollection
{
    private readonly List<TextureHandle> _textures = [];
    private readonly HashSet<TextureHandle> _textureSet = [];
    private readonly List<AudioClip> _clips = [];
    private readonly HashSet<AudioClip> _clipSet = [];

    /// <summary>Adds one texture unless it was already declared.</summary>
    public void Add(TextureHandle texture)
    {
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
    /// Adds one clip unless it was already declared. A clip whose format the host streams rather
    /// than holds in memory reserves nothing: declaring it is harmless and preloads no samples.
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

    internal IReadOnlyList<TextureHandle> Textures => _textures;

    internal IReadOnlyList<AudioClip> Clips => _clips;
}

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

    // Set on a collection gathered only to read, as Scene.CollectPreloads and a pool's first-take check
    // gather one. Collecting a pool into it does not forward the pool.
    internal bool IsProbe { get; init; }

    // The scene gathering this collection, which builds the shared entity pools declared into it. Typed as
    // object because scenes live above this assembly. Set only while the scene collects, because a host
    // keeps a scene's preloads past the scene's life.
    internal object? GatheringScene { get; set; }

    /// <summary>
    /// Adds one texture unless it was already declared. A default handle is ignored, and so are the
    /// engine's own textures, the white texel and the default font's page, which belong to the host.
    /// </summary>
    public void Add(TextureHandle texture)
    {
        if (texture.Name is null || texture.IsEngineOwned)
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

    /// <summary>Whether this collection declares <paramref name="texture"/>.</summary>
    public bool Contains(TextureHandle texture) => _textureSet.Contains(texture);

    /// <summary>Whether this collection declares <paramref name="clip"/>.</summary>
    public bool Contains(AudioClip clip) => _clipSet.Contains(clip);

    /// <summary>Whether this collection declares <paramref name="shader"/>, which a renderer's material adds.</summary>
    public bool Contains(Shader shader) => _shaderSet.Contains(shader);

    // Every texture and clip another collection holds, in its order.
    internal void Add(AssetCollection other)
    {
        foreach (TextureHandle texture in other._textures)
        {
            Add(texture);
        }

        foreach (AudioClip clip in other._clips)
        {
            Add(clip);
        }
    }

    /// <summary>
    /// Adds a material's shader and every texture set on it, ignoring any already declared. A renderer's
    /// material is collected with its entity. This declares one the entity assigns later.
    /// </summary>
    /// <example>
    /// <code>
    /// protected override void CollectAssets(AssetCollection assets) =&gt; assets.Add(_palette);
    /// </code>
    /// </example>
    public void Add(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (_shaderSet.Add(material.Shader))
        {
            _shaders.Add(material.Shader);
        }

        for (int i = 0; i < material.Shader.Parameters.Length; i++)
        {
            if (material.TryGetTexture(i, out TextureHandle texture))
            {
                Add(texture);
            }
        }
    }

    // Whether preloading this collection would load anything. A streamed clip loads nothing ahead.
    internal bool HoldsPreloads
    {
        get
        {
            if (_textures.Count > 0 || _shaders.Count > 0)
            {
                return true;
            }

            foreach (AudioClip clip in _clips)
            {
                if (!clip.IsStreamed)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal IReadOnlyList<TextureHandle> Textures => _textures;

    internal IReadOnlyList<Shader> Shaders => _shaders;

    internal IReadOnlyList<AudioClip> Clips => _clips;
}

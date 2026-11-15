using System.ComponentModel;
using System.Runtime.CompilerServices;
using Capsule.Animation;
using Capsule.Audio;
using Capsule.Rendering;

namespace Capsule.Assets;

/// <summary>
/// Assets an object graph asks the host to preload, kept once each in first-declaration order.
/// Collecting names does no device or file-system work.
/// </summary>
/// <remarks>
/// A scene's collection holds every <c>CapsuleAssets</c> member the code of each scene, entity and
/// component class it reaches reads, and of their base classes. A <c>CollectAssets</c> hook adds what
/// that code does not name, such as an asset chosen by data.
/// </remarks>
public sealed class AssetCollection
{
    // What each class's own code names, keyed by the class, or by its definition for a generic class.
    // Each logic assembly's module initializer adds its classes once. The table holds no class alive, and an
    // unloaded assembly's entries go with it.
    private static readonly ConditionalWeakTable<Type, Action<AssetCollection>> ClassAssets = new();

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

    /// <summary>Adds the texture a sprite is cut from unless it was already declared.</summary>
    public void Add(Sprite sprite) => Add(sprite.Texture);

    /// <summary>Adds the texture of every frame a clip plays, ignoring any already declared.</summary>
    public void Add(SpriteClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        foreach (Sprite frame in clip.Frames)
        {
            Add(frame.Texture);
        }
    }

    /// <summary>Adds every texture a variant draws from, ignoring any already declared.</summary>
    public void Add(SpriteVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        Add(variant.Textures);
    }

    /// <summary>Adds every page of a font, ignoring any already declared. The default font's page belongs to the host.</summary>
    public void Add(BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);

        Add(font.Pages);
    }

    /// <summary>Adds one shader unless it was already declared.</summary>
    public void Add(Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);

        if (_shaderSet.Add(shader))
        {
            _shaders.Add(shader);
        }
    }

    /// <summary>Whether this collection declares <paramref name="texture"/>.</summary>
    public bool Contains(TextureHandle texture) => _textureSet.Contains(texture);

    /// <summary>Whether this collection declares <paramref name="clip"/>.</summary>
    public bool Contains(AudioClip clip) => _clipSet.Contains(clip);

    /// <summary>Whether this collection declares <paramref name="shader"/>, which a renderer's material adds.</summary>
    public bool Contains(Shader shader) => _shaderSet.Contains(shader);

    // Every texture, clip and shader another collection holds, in its order.
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

        foreach (Shader shader in other._shaders)
        {
            Add(shader);
        }
    }

    /// <summary>Records what one class's own code names, for every scene to preload with it. Called by generated code.</summary>
    /// <param name="type">The class, or the definition of a generic class.</param>
    /// <param name="collect">Adds what the class names, and nothing its base classes name.</param>
    /// <exception cref="InvalidOperationException">The class already has a record.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterClassAssets(Type type, Action<AssetCollection> collect)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(collect);

        if (!ClassAssets.TryAdd(type, collect))
        {
            throw new InvalidOperationException(
                $"{type.Name} already has its assets recorded. Each class is recorded once, by its own assembly's generated code.");
        }
    }

    // Adds what the code of the owner's class and each of its bases names. A collection runs this before each
    // object's own hook.
    internal void AddClassAssets(object owner)
    {
        for (Type? type = owner.GetType(); type is not null; type = type.BaseType)
        {
            Type key = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;
            if (ClassAssets.TryGetValue(key, out Action<AssetCollection>? collect))
            {
                collect(this);
            }
        }
    }

    /// <summary>
    /// Adds a material's shader and every texture set on it, ignoring any already declared.
    /// </summary>
    /// <remarks>
    /// A renderer's material is collected with its entity. This declares one the entity assigns later.
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void CollectAssets(AssetCollection assets) =&gt; assets.Add(_palette);
    /// </code>
    /// </example>
    public void Add(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        Add(material.Shader);

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

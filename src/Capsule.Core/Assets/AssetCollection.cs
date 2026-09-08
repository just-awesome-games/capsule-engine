namespace Capsule.Assets;

/// <summary>
/// Assets an object graph asks the host to preload, kept once in first-declaration order.
/// Pure names only; collecting them performs no device or file-system work.
/// </summary>
public sealed class AssetCollection
{
    private readonly List<TextureHandle> _textures = [];
    private readonly HashSet<TextureHandle> _textureSet = [];

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

    internal IReadOnlyList<TextureHandle> Textures => _textures;
}

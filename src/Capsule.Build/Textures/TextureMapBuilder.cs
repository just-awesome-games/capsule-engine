using System.Text.Json;
using Capsule.Assets;

namespace Capsule.Build.Textures;

/// <summary>
/// The shipped texture map as the atlas and texture steps fill it: every packed texture's page and
/// offset, every other texture's non-default facts, and every page's.
/// </summary>
internal sealed class TextureMapBuilder
{
    private readonly SortedDictionary<string, TextureEntryJson> _textures = new(StringComparer.Ordinal);

    private readonly SortedDictionary<string, TextureEntryJson> _pages = new(StringComparer.Ordinal);

    internal void AddTexture(string key, TextureEntryJson entry) => _textures.Add(key, entry);

    internal void AddPage(string page, TextureEntryJson facts) => _pages.Add(page, facts);

    /// <summary>Ships the map at <see cref="TextureMapJson.ShippedPath"/>, only when some texture has a non-default run-time fact.</summary>
    internal void Ship(OutputFiles shipped)
    {
        if (_textures.Count == 0)
        {
            return;
        }

        TextureMapJson map = new() { Textures = _textures, Pages = _pages.Count > 0 ? _pages : null };
        Directory.CreateDirectory(shipped.Root);
        shipped.Claim(TextureMapJson.ShippedPath, "the texture map");
        AtomicFile.Write(Path.Combine(shipped.Root, TextureMapJson.ShippedPath), path =>
        {
            using FileStream file = File.Create(path);
            JsonSerializer.Serialize(file, map, TextureMapJsonContext.Default.TextureMapJson);
        });
    }
}

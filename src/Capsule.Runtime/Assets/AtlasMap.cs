using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Assets;

namespace Capsule.Runtime.Assets;

// Where a packed texture's texels are: the page that holds it and the texel its (0, 0) landed on.
internal readonly record struct AtlasSlot(TextureHandle Page, int X, int Y);

// The map the build wrote beside the pages: which texture handles are served from a page instead of
// a file of their own. Read once at boot. A game with no atlas has no map, and every handle resolves
// to itself.
internal sealed class AtlasMap
{
    internal static readonly AtlasMap Empty = new([]);

    private const string MapPath = "assets/textures/atlases.json";

    private readonly Dictionary<TextureHandle, AtlasSlot> _slots;

    private AtlasMap(Dictionary<TextureHandle, AtlasSlot> slots) => _slots = slots;

    // The map shipped under the textures root, or Empty when none shipped.
    internal static AtlasMap Load(HostPlatform platform)
    {
        Stream file;
        try
        {
            file = platform.OpenContent(MapPath);
        }
        catch (IOException missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
        {
            return Empty;
        }

        using (file)
        {
            return Parse(file, MapPath);
        }
    }

    // Throws InvalidDataException when the map is malformed. A shipped map is derived, and a defect in
    // it means the build went wrong.
    internal static AtlasMap Parse(Stream json, string path)
    {
        AtlasMapJson? raw;
        try
        {
            raw = JsonSerializer.Deserialize(json, AtlasMapJsonContext.Default.AtlasMapJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The atlas map '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (raw?.Textures is not { } textures)
        {
            throw new InvalidDataException($"The atlas map '{path}' declares no \"textures\".");
        }

        Dictionary<TextureHandle, AtlasSlot> slots = new(textures.Count);
        foreach ((string key, AtlasEntryJson? entry) in textures)
        {
            if (entry?.Page is not { Length: > 0 } page || entry.X < 0 || entry.Y < 0)
            {
                throw new InvalidDataException($"The atlas map '{path}' gives '{key}' no page or a negative offset.");
            }

            slots.Add(new TextureHandle(key, ".png"), new AtlasSlot(new TextureHandle(page, ".png"), entry.X, entry.Y));
        }

        return new AtlasMap(slots);
    }

    internal bool TryGet(in TextureHandle handle, out AtlasSlot slot) => _slots.TryGetValue(handle, out slot);

    // What the store must hold for these handles: each packed handle becomes its page, and the rest
    // stay themselves. Returns the list unchanged when nothing is packed, and a game with no atlas
    // allocates nothing.
    internal IReadOnlyList<TextureHandle> Residency(IReadOnlyList<TextureHandle> handles)
    {
        if (_slots.Count == 0)
        {
            return handles;
        }

        TextureHandle[] pages = new TextureHandle[handles.Count];
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i] = _slots.TryGetValue(handles[i], out AtlasSlot slot) ? slot.Page : handles[i];
        }

        return pages;
    }
}

internal sealed class AtlasMapJson
{
    [JsonPropertyName("textures")]
    public Dictionary<string, AtlasEntryJson?>? Textures { get; set; }
}

internal sealed class AtlasEntryJson
{
    [JsonPropertyName("page")]
    public string? Page { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

// Reflection-based serialization is off solution-wide, so this generated context reads the map.
[JsonSerializable(typeof(AtlasMapJson))]
internal sealed partial class AtlasMapJsonContext : JsonSerializerContext;

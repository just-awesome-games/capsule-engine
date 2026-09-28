using System.Text.Json;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Runtime.Assets;

// Where a packed texture's texels are: the page that holds it and the texel its (0, 0) landed on.
internal readonly record struct AtlasSlot(TextureHandle Page, int X, int Y);

// A texture file's non-default settings. The default is four channels sampled as the scene says.
internal readonly record struct TextureFacts(bool SingleChannel, TextureSampling? Sampling);

// The map the build shipped: which texture handles are served from a page instead of a file of their
// own, and which files carry a non-default format or sampling. Read once at boot. A game with every
// texture unpacked and default ships no map, and every handle resolves to itself.
internal sealed class TextureMap
{
    internal static readonly TextureMap Empty = new([], []);

    private const string MapPath = "assets/textures.json";

    private readonly Dictionary<TextureHandle, AtlasSlot> _slots;

    private readonly Dictionary<TextureHandle, TextureFacts> _facts;

    private TextureMap(Dictionary<TextureHandle, AtlasSlot> slots, Dictionary<TextureHandle, TextureFacts> facts)
    {
        _slots = slots;
        _facts = facts;
    }

    // The map shipped at the content root, or Empty when none shipped.
    internal static TextureMap Load(HostPlatform platform)
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
    internal static TextureMap Parse(Stream json, string path)
    {
        TextureMapJson? raw;
        try
        {
            raw = JsonSerializer.Deserialize(json, TextureMapJsonContext.Default.TextureMapJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The texture map '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (raw?.Textures is not { } textures)
        {
            throw new InvalidDataException($"The texture map '{path}' declares no \"textures\".");
        }

        Dictionary<TextureHandle, AtlasSlot> slots = new(textures.Count);
        Dictionary<TextureHandle, TextureFacts> facts = [];
        foreach ((string key, TextureEntryJson? entry) in textures)
        {
            TextureHandle handle = new(key, ".png");
            if (entry?.Page is { Length: > 0 } page)
            {
                if (entry.X is not >= 0 || entry.Y is not >= 0)
                {
                    throw new InvalidDataException($"The texture map '{path}' gives '{key}' a page and no offset or a negative one.");
                }

                slots.Add(handle, new AtlasSlot(new TextureHandle(page, ".png"), entry.X.Value, entry.Y.Value));
            }
            else if (entry is null || (entry.Format is null && entry.Sampling is null))
            {
                throw new InvalidDataException($"The texture map '{path}' gives '{key}' no page and no setting.");
            }
            else
            {
                facts.Add(handle, Facts(entry));
            }
        }

        foreach ((string page, TextureEntryJson? entry) in raw.Pages ?? new Dictionary<string, TextureEntryJson>())
        {
            facts.Add(new TextureHandle(page, ".png"), Facts(entry ?? new TextureEntryJson()));
        }

        return new TextureMap(slots, facts);
    }

    internal bool TryGet(in TextureHandle handle, out AtlasSlot slot) => _slots.TryGetValue(handle, out slot);

    // The settings of a file the store loads: a page or an unpacked texture.
    internal TextureFacts Facts(in TextureHandle file) => _facts.GetValueOrDefault(file);

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

    private static TextureFacts Facts(TextureEntryJson entry) => new(
        entry.Format == TextureFormatSetting.R8,
        entry.Sampling switch
        {
            TextureSamplingSetting.Point => TextureSampling.Point,
            TextureSamplingSetting.Linear => TextureSampling.Linear,
            _ => null,
        });
}

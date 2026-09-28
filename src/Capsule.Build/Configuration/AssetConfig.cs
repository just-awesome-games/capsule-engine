using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Capsule.Assets;

namespace Capsule.Build.Configuration;

/// <summary>Reads the optional config and atlas files under <c>Assets/</c> and resolves every texture's settings.</summary>
internal static class AssetConfig
{
    private static AssetConfigJsonContext Context => AssetConfigJsonContext.Default;

    /// <summary>Every declared atlas by its name, with the file that declares it.</summary>
    internal static Dictionary<string, (string Path, AtlasConfigJson? Config)> ReadAtlases(BuildPass pass)
    {
        // A defective file still declares its atlas with a null config. A texture naming it then
        // reads as declared, and the file reports only its own defect.
        Dictionary<string, (string Path, AtlasConfigJson? Config)> atlases = new(StringComparer.Ordinal);
        foreach (Source file in pass.Of(AssetType.Atlases))
        {
            string name = file.Key[(file.Key.LastIndexOf('/') + 1)..];
            if (atlases.TryGetValue(name, out (string Path, AtlasConfigJson? Config) first))
            {
                pass.Fail(file.Path, $"declares the atlas \"{name}\", which '{first.Path}' already declares. An atlas is named by its file name, so rename or delete one.");
                continue;
            }

            AtlasConfigJson? config;
            try
            {
                config = Parse(file, Context.AtlasConfigJson, AtlasConfigJson.Shape);
                if (config.MaxSize is { } maxSize && (maxSize <= 0 || maxSize > AtlasConfigJson.LargestMaxSize || !int.IsPow2(maxSize)))
                {
                    throw new FormatException($"sets \"maxSize\" to {maxSize}. {AtlasConfigJson.Shape}");
                }
            }
            catch (FormatException ex)
            {
                pass.Fail(file.Path, ex.Message);
                config = null;
            }

            atlases.Add(name, (file.Path, config));
        }

        return atlases;
    }

    /// <summary>Every texture's resolved settings by texture key, with every setting filled.</summary>
    /// <param name="fontPages">The keys of textures a font names as its page. An inherited atlas is ignored for one.</param>
    /// <param name="atlases">Every declared atlas. A texture's <c>atlas</c> must name one.</param>
    /// <param name="setBy">The file under <c>Assets/</c> that set each value, by texture key and setting name.</param>
    internal static Dictionary<string, TextureConfigJson> ResolveTextures(
        BuildPass pass,
        HashSet<string> fontPages,
        Dictionary<string, (string Path, AtlasConfigJson? Config)> atlases,
        out Dictionary<(string Texture, string Setting), string> setBy)
    {
        // Every asset by its key and extension, which is how a sidecar keys the file it names.
        Dictionary<string, Source> files = pass.Keyed
            .Where(static source => source.Type != AssetType.Configs && source.Type != AssetType.Atlases)
            .ToDictionary(static source => source.Key + source.Extension, StringComparer.Ordinal);

        // Folder files by the folder they configure, "" for Assets/ itself and "a/b/" below it. A sidecar
        // reads as a folder file holding only its asset's kind.
        Dictionary<string, (string From, FolderConfigJson File)> folders = new(StringComparer.Ordinal);
        Dictionary<string, (string From, FolderConfigJson File)> sidecars = new(StringComparer.Ordinal);

        int failures = pass.Failures;
        foreach ((Source config, FolderConfigJson file) in pass.Each(pass.Of(AssetType.Configs), source => Read(source, files, fontPages, atlases)))
        {
            string from = Keys.Below(pass.Requests.AssetRoot, config.Path);
            (IsFolderFile(config) ? folders : sidecars).Add(config.Key, (from, file));
        }

        // Each setting resolves on its own: the engine default, then each folder's .config.json from
        // Assets/ down, then the sidecar. The nearest value wins. A defective file contributes nothing.
        bool configsRead = pass.Failures == failures;
        setBy = [];
        Dictionary<string, TextureConfigJson> resolved = new(StringComparer.Ordinal);
        foreach (Source texture in pass.Of(AssetType.Textures))
        {
            TextureConfigJson settings = new()
            {
                Atlas = new AtlasSetting(null),
                Format = TextureFormatSetting.Rgba,
                Sampling = TextureSamplingSetting.Scene,
            };
            bool page = fontPages.Contains(texture.Key);
            // Every folder from Assets/ down to the texture's own, each ending at a separator.
            int end = 0;
            while (true)
            {
                if (folders.TryGetValue(texture.Key[..end], out (string From, FolderConfigJson File) folder))
                {
                    Apply(settings, folder.File.Texture, texture.Key, folder.From, page, setBy);
                }

                int slash = texture.Key.IndexOf('/', end);
                if (slash < 0)
                {
                    break;
                }

                end = slash + 1;
            }

            if (sidecars.TryGetValue(texture.Key + texture.Extension, out (string From, FolderConfigJson File) own))
            {
                Apply(settings, own.File.Texture, texture.Key, own.From, page, setBy);
            }

            resolved.Add(texture.Key, settings);
        }

        // A config file that failed may be the one naming an atlas, which would then read as unused.
        if (configsRead)
        {
            HashSet<string?> used = [.. resolved.Values.Select(static texture => texture.Atlas!.Value.Name)];
            foreach ((string name, (string path, _)) in atlases)
            {
                if (!used.Contains(name))
                {
                    pass.Fail(path, $"declares the atlas \"{name}\", and no texture's \"atlas\" setting names it. Set \"atlas\": \"{name}\" in a texture's config, or delete this file.");
                }
            }
        }

        return resolved;
    }

    /// <summary>The clause a texture member's summary ends in, or null when every setting is its default.</summary>
    internal static string? Describe(string key, TextureConfigJson texture, Dictionary<(string Texture, string Setting), string> setBy)
    {
        // As: with atlas "game" from <c>.config.json</c> and format "r8" from <c>glow.png.config.json</c>.
        List<string> parts = [];
        Describe(parts, key, "atlas", texture.Atlas!.Value.Name is { } atlas ? $"\"{atlas}\"" : null, setBy);
        Describe(parts, key, "format", texture.Format!.Value == default ? null : Spell(texture.Format.Value, Context.TextureFormatSetting), setBy);
        Describe(parts, key, "sampling", texture.Sampling!.Value == default ? null : Spell(texture.Sampling.Value, Context.TextureSamplingSetting), setBy);

        return parts.Count switch
        {
            0 => null,
            1 => "with " + parts[0],
            _ => $"with {string.Join(", ", parts[..^1])} and {parts[^1]}",
        };
    }

    private static bool IsFolderFile(Source config) => config.Key.Length == 0 || config.Key[^1] == '/';

    // A font's page never packs, so an atlas it inherits is ignored. Its own sidecar setting one fails earlier.
    private static void Apply(
        TextureConfigJson settings,
        TextureConfigJson? config,
        string key,
        string from,
        bool fontPage,
        Dictionary<(string Texture, string Setting), string> setBy)
    {
        if (config?.Atlas is { } atlas && !fontPage)
        {
            settings.Atlas = atlas;
            setBy[(key, "atlas")] = from;
        }

        if (config?.Format is { } format)
        {
            settings.Format = format;
            setBy[(key, "format")] = from;
        }

        if (config?.Sampling is { } sampling)
        {
            settings.Sampling = sampling;
            setBy[(key, "sampling")] = from;
        }
    }

    // One part per non-default setting: its name, its spelled value and the file that set it.
    private static void Describe(List<string> parts, string key, string setting, string? spelled, Dictionary<(string Texture, string Setting), string> setBy)
    {
        if (spelled is not null)
        {
            parts.Add($"{setting} {spelled} from <c>{SecurityElement.Escape(setBy[(key, setting)])}</c>");
        }
    }

    // A value as a config file spells it, quoted.
    private static string Spell<T>(T value, JsonTypeInfo<T> type) => JsonSerializer.Serialize(value, type);

    // Parses a file, and checks a sidecar against the asset file it names and any atlas against the declared ones.
    private static FolderConfigJson Read(
        Source config,
        Dictionary<string, Source> files,
        HashSet<string> fontPages,
        Dictionary<string, (string Path, AtlasConfigJson? Config)> atlases)
    {
        FolderConfigJson file;
        if (IsFolderFile(config))
        {
            file = Parse(config, Context.FolderConfigJson, FolderConfigJson.Shape);
        }
        else
        {
            string named = Path.GetFileName(config.Path)[..^config.Extension.Length];
            if (!files.TryGetValue(config.Key, out Source asset))
            {
                // A key holds no '.', so the first one starts the extension the sidecar names.
                int dot = config.Key.IndexOf('.', StringComparison.Ordinal);
                string key = dot < 0 ? config.Key : config.Key[..dot];
                string[] suggested = [.. files.Values.Where(file => file.Key == key).Select(static file => $"\"{Path.GetFileName(file.Path)}.config.json\"").Order(StringComparer.Ordinal)];
                throw new FormatException(suggested.Length > 0
                    ? $"configures \"{named}\", and no asset file beside it is named that. A sidecar names its asset's whole file name, so rename it to {List(suggested, "or")}."
                    : $"configures \"{named}\", and no asset file beside it is named that. A <file>.<ext>.config.json configures that file beside it, and a .config.json configures the whole folder. Rename or delete it.");
            }

            file = asset.Type == AssetType.Textures
                ? new() { Texture = Parse(config, Context.TextureConfigJson, TextureConfigJson.Shape) }
                : throw new FormatException(
                    $"configures the {asset.Type.Suffix.ToLowerInvariant()} \"{named}\", and a {asset.Type.Suffix.ToLowerInvariant()} has no settings. {FolderConfigJson.Kinds} Delete this file.");

            if (file.Texture?.Atlas is not null && fontPages.Contains(asset.Key))
            {
                throw new FormatException("sets \"atlas\" for a font's page, which never packs. Remove \"atlas\" from this file.");
            }
        }

        if (file.Texture?.Atlas?.Name is { } atlas && !atlases.ContainsKey(atlas))
        {
            throw new FormatException(
                $"sets \"atlas\" to \"{atlas}\", and no \"{atlas}.atlas.json\" under Assets/ declares that atlas. Add that file, or name a declared atlas.");
        }

        return file;
    }

    /// <summary>Reads <paramref name="config"/> as one <typeparamref name="T"/>.</summary>
    /// <param name="shape">What the file holds and every setting it may set, for a refusal.</param>
    /// <exception cref="FormatException">The JSON is malformed, or a kind, setting or value is not one this build knows.</exception>
    private static T Parse<T>(Source config, JsonTypeInfo<T> type, string shape)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(config.Path));
        }
        catch (JsonException ex)
        {
            throw new FormatException($"is not valid JSON at line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1}.", ex);
        }

        using (document)
        {
            // A present null would read as an omitted setting and silently inherit.
            if (NullAt(document.RootElement, "$") is { } path)
            {
                throw new FormatException(
                    $"has a null at {path}. Omit a setting to inherit it, or write its default, such as false for \"atlas\". {shape}");
            }

            try
            {
                return document.Deserialize(type) ?? throw new FormatException($"is not a JSON object. {shape}");
            }
            catch (JsonException ex)
            {
                throw new FormatException($"has an unknown kind, setting or value at {ex.Path}. {shape}", ex);
            }
        }
    }

    // The path of the first null in element, or null when it holds none.
    private static string? NullAt(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return path;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (NullAt(property.Value, $"{path}.{property.Name}") is { } found)
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (NullAt(item, $"{path}[{index++}]") is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    // "a", "a" or "b", "a", "b" or "c".
    private static string List(string[] values, string conjunction) =>
        values.Length < 2 ? string.Concat(values) : $"{string.Join(", ", values[..^1])} {conjunction} {values[^1]}";
}

// Reflection-based serialization is off solution-wide. A kind, setting or value the format does not
// declare fails the file.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(FolderConfigJson))]
[JsonSerializable(typeof(TextureConfigJson))]
[JsonSerializable(typeof(AtlasConfigJson))]
[JsonSerializable(typeof(TextureFormatSetting))]
[JsonSerializable(typeof(TextureSamplingSetting))]
internal sealed partial class AssetConfigJsonContext : JsonSerializerContext;

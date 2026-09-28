using Capsule.Build.Atlases;
using Capsule.Build.Configuration;

namespace Capsule.Build.Textures;

/// <summary>
/// Reads every <c>.config.json</c> and sets <see cref="BuildPass.TextureSettings"/>, each texture's
/// settings resolved from the engine defaults, the folder files above it and its sidecar.
/// </summary>
internal static class TextureSettingsStep
{
    internal static void Run(BuildPass pass)
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
        foreach ((Source config, FolderConfigJson file) in pass.Each(pass.Of(AssetType.Configs), source => Read(pass, source, files)))
        {
            string from = Keys.Below(pass.Requests.AssetRoot, config.Path);
            (IsFolderFile(config) ? folders : sidecars).Add(config.Key, (from, file));
        }

        // Each setting resolves on its own: the engine default, then each folder's .config.json from
        // Assets/ down, then the sidecar. The nearest value wins. A defective file contributes nothing.
        bool configsRead = pass.Failures == failures;
        Dictionary<string, ResolvedTexture> resolved = new(StringComparer.Ordinal);
        foreach (Source texture in pass.Of(AssetType.Textures))
        {
            ResolvedTexture settings = new();
            bool page = pass.FontPages.Contains(texture.Key);
            // Every folder from Assets/ down to the texture's own, each ending at a separator.
            int end = 0;
            while (true)
            {
                if (folders.TryGetValue(texture.Key[..end], out (string From, FolderConfigJson File) folder))
                {
                    Apply(settings, folder.File.Texture, folder.From, page);
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
                Apply(settings, own.File.Texture, own.From, page);
            }

            resolved.Add(texture.Key, settings);
        }

        // Checked here, where every texture's atlas is known. A config file that failed may be the one
        // naming an atlas, which would then read as unused.
        if (configsRead)
        {
            HashSet<string?> used = [.. resolved.Values.Select(static texture => texture.Atlas)];
            foreach ((string name, DeclaredAtlas declared) in pass.Atlases)
            {
                if (!used.Contains(name))
                {
                    pass.Fail(declared.Path, $"declares the atlas \"{name}\", and no texture's \"atlas\" setting names it. Set \"atlas\": \"{name}\" in a texture's config, or delete this file.");
                }
            }
        }

        pass.TextureSettings = resolved;
    }

    private static bool IsFolderFile(Source config) => config.Key.Length == 0 || config.Key[^1] == '/';

    // A font's page never packs, so an atlas it inherits is ignored. Its own sidecar setting one fails earlier.
    private static void Apply(ResolvedTexture settings, TextureConfigJson? config, string from, bool fontPage)
    {
        if (config?.Atlas is { } atlas && !fontPage)
        {
            settings.Atlas = atlas.Name;
            settings.AtlasSetBy = from;
        }

        if (config?.Format is { } format)
        {
            settings.Format = format;
            settings.FormatSetBy = from;
        }

        if (config?.Sampling is { } sampling)
        {
            settings.Sampling = sampling;
            settings.SamplingSetBy = from;
        }
    }

    // Parses a file, and checks a sidecar against the asset file it names and any atlas against the declared ones.
    private static FolderConfigJson Read(BuildPass pass, Source config, Dictionary<string, Source> files)
    {
        FolderConfigJson file;
        if (IsFolderFile(config))
        {
            file = AssetConfigJsonContext.Read(config, AssetConfigJsonContext.Default.FolderConfigJson, FolderConfigJson.Shape);
        }
        else
        {
            string named = Path.GetFileName(config.Path)[..^config.Extension.Length];
            if (!files.TryGetValue(config.Key, out Source asset))
            {
                int dot = Keys.SidecarExtension(config.Key);
                string key = dot < 0 ? config.Key : config.Key[..dot];
                string[] suggested = [.. files.Values.Where(file => file.Key == key).Select(static file => $"\"{Path.GetFileName(file.Path)}.config.json\"").Order(StringComparer.Ordinal)];
                throw new FormatException(suggested.Length > 0
                    ? $"configures \"{named}\", and no asset file beside it is named that. A sidecar names its asset's whole file name, so rename it to {List(suggested, "or")}."
                    : $"configures \"{named}\", and no asset file beside it is named that. A <file>.<ext>.config.json configures that file beside it, and a .config.json configures the whole folder. Rename or delete it.");
            }

            file = asset.Type == AssetType.Textures
                ? new() { Texture = AssetConfigJsonContext.Read(config, AssetConfigJsonContext.Default.TextureSidecarJson, TextureConfigJson.Shape) }
                : throw new FormatException(
                    $"configures the {asset.Type.Suffix.ToLowerInvariant()} \"{named}\", and a {asset.Type.Suffix.ToLowerInvariant()} has no settings. {FolderConfigJson.Kinds} Delete this file.");

            if (file.Texture?.Atlas is not null && pass.FontPages.Contains(asset.Key))
            {
                throw new FormatException("sets \"atlas\" for a font's page, which never packs. Remove \"atlas\" from this file.");
            }
        }

        if (file.Texture?.Atlas?.Name is { } atlas && !pass.Atlases.ContainsKey(atlas))
        {
            throw new FormatException(
                $"sets \"atlas\" to \"{atlas}\", and no \"{atlas}.atlas.json\" under Assets/ declares that atlas. Add that file, or name a declared atlas.");
        }

        return file;
    }

    // "a", "a" or "b", "a", "b" or "c".
    private static string List(string[] values, string conjunction) =>
        values.Length < 2 ? string.Concat(values) : $"{string.Join(", ", values[..^1])} {conjunction} {values[^1]}";
}

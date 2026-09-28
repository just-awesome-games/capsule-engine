using System.Globalization;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Build.Textures;

namespace Capsule.Build.Atlases;

/// <summary>
/// Packs every texture whose <c>atlas</c> setting names a declared atlas onto that atlas's pages, and
/// sets <see cref="BuildPass.TextureMap"/> holding every packed texture's page and offset and every
/// page's non-default facts.
/// </summary>
internal static class AtlasStep
{
    /// <summary>Texels of border duplicated outward on every side of a member.</summary>
    internal const int Extrude = 1;

    // Where every page ships below assets/, as atlases/game.0.png. No key holds a '.', so no asset
    // ships at a page's path.
    private const string PageDirectory = "atlases/";

    internal static void Run(BuildPass pass)
    {
        TextureMapBuilder map = new();
        pass.TextureMap = map;

        SortedDictionary<string, List<string>> packing = new(StringComparer.Ordinal);
        foreach ((string key, ResolvedTexture texture) in pass.TextureSettings)
        {
            if (texture.Atlas is { } atlas)
            {
                if (!packing.TryGetValue(atlas, out List<string>? members))
                {
                    packing.Add(atlas, members = []);
                }

                members.Add(key);
            }
        }

        if (packing.Count == 0)
        {
            return;
        }

        string stamps = pass.CacheDirectory("atlases");
        TextureMapJson previous = Read(Path.Combine(pass.Shipped.Root, TextureMapJson.ShippedPath));

        foreach ((string atlas, List<string> members) in packing)
        {
            // An undeclared atlas or a defective atlas file already failed the build.
            if (!pass.Atlases.TryGetValue(atlas, out DeclaredAtlas declared) || declared.Config is not { } config)
            {
                continue;
            }

            // A build that changed nothing the stamp covers reuses the last map's placements.
            int maxSize = config.MaxSize ?? AtlasConfigJson.DefaultMaxSize;
            members.Sort(StringComparer.Ordinal);
            string stamp = Stamp(maxSize, members, pass.TextureSettings, pass.Textures);
            string stampPath = Path.Combine(stamps, atlas + ".stamp");
            TextureMapJson? packed = File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp
                ? Previous(previous, atlas, pass.Shipped)
                : null;

            if (packed is null)
            {
                packed = Repack(pass, atlas, maxSize, declared.Path, members);
                if (packed is null)
                {
                    continue;
                }

                AtomicFile.WriteText(stampPath, stamp);
                pass.Progress($"atlas {atlas}", $"{members.Count} texture(s) packed on {packed.Textures!.Values.DistinctBy(static entry => entry.Page).Count()} page(s)");
            }
            else
            {
                pass.Progress($"atlas {atlas}", "up to date");
            }

            foreach ((string key, TextureEntryJson entry) in packed.Textures!)
            {
                map.AddTexture(key, entry);
            }

            foreach ((string page, TextureEntryJson facts) in packed.Pages!)
            {
                map.AddPage(page, facts);
            }
        }
    }

    private static string PageName(string atlas, int page) =>
        $"{PageDirectory}{atlas}.{page.ToString(CultureInfo.InvariantCulture)}";

    private static string PagePath(ShippedFiles shipped, string page) =>
        shipped.Claim(page + ".png", $"the atlas page \"{page}\"");

    // The map at path, or an empty one when none is there or it cannot be read. It is the build's own
    // cache, so malformed JSON is a stale map and not a defect.
    private static TextureMapJson Read(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);

            return JsonSerializer.Deserialize(file, TextureMapJsonContext.Default.TextureMapJson) ?? new TextureMapJson();
        }
        catch (Exception ex) when (ex is JsonException || BuildPass.IsReportable(ex))
        {
            return new TextureMapJson();
        }
    }

    // The atlas's placements and page facts from the last map written, or null when the map lacks the
    // atlas or one of its pages is gone. Either means it must be packed again.
    private static TextureMapJson? Previous(TextureMapJson previous, string atlas, ShippedFiles shipped)
    {
        string prefix = PageDirectory + atlas + ".";
        Dictionary<string, TextureEntryJson> textures = new(StringComparer.Ordinal);
        foreach ((string key, TextureEntryJson entry) in previous.Textures ?? new Dictionary<string, TextureEntryJson>())
        {
            if (entry.Page is { } page && page.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (!File.Exists(PagePath(shipped, page)))
                {
                    return null;
                }

                textures.Add(key, entry);
            }
        }

        Dictionary<string, TextureEntryJson> pages = new(StringComparer.Ordinal);
        foreach ((string page, TextureEntryJson facts) in previous.Pages ?? new Dictionary<string, TextureEntryJson>())
        {
            if (page.StartsWith(prefix, StringComparison.Ordinal))
            {
                pages.Add(page, facts);
            }
        }

        return textures.Count > 0 ? new TextureMapJson { Textures = textures, Pages = pages } : null;
    }

    // A repack is decided by this packer's build, the atlas's own settings and every member's key, length,
    // write time, format and sampling. Reading and hashing every texture on every build costs more than
    // the repack it saves.
    private static string Stamp(int maxSize, List<string> members, IReadOnlyDictionary<string, ResolvedTexture> settings, IReadOnlyDictionary<string, Source> textures)
    {
        StringBuilder stamp = new StringBuilder("build|").AppendLine(typeof(AtlasPacker).Assembly.ManifestModule.ModuleVersionId.ToString())
            .Append("maxSize|").AppendLine(maxSize.ToString(CultureInfo.InvariantCulture));
        foreach (string key in members)
        {
            FileInfo file = new(textures[key].Path);
            stamp.Append(key).Append('|').Append(file.Length)
                .Append('|').Append(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(settings[key].Format.ToString())
                .Append('|').AppendLine(settings[key].Sampling.ToString());
        }

        return stamp.ToString();
    }

    // Decodes, packs and writes every page of one atlas, one run of pages per format and sampling with
    // the defaults first. Reports every member that cannot be packed before returning null.
    private static TextureMapJson? Repack(
        BuildPass pass,
        string atlas,
        int maxSize,
        string atlasPath,
        List<string> members)
    {
        IReadOnlyDictionary<string, ResolvedTexture> settings = pass.TextureSettings;
        int failures = pass.Failures;
        Dictionary<string, Texels> images = pass.Each(
            members.Select(key => pass.Textures[key]),
            member =>
            {
                Texels image = TexturePixels.Decode(member.Path, settings[member.Key].Format);

                return image.Width + (2 * Extrude) > maxSize || image.Height + (2 * Extrude) > maxSize
                    ? throw new FormatException(
                        $"is {image.Width}x{image.Height}, which with its {Extrude}-texel border exceeds the {maxSize}-texel page of the atlas \"{atlas}\". Set \"atlas\": false for it in its config, or raise \"maxSize\" in '{atlasPath}'.")
                    : image;
            })
            .ToDictionary(static decoded => decoded.Source.Key, static decoded => decoded.Value, StringComparer.Ordinal);

        if (pass.Failures > failures)
        {
            return null;
        }

        Dictionary<string, TextureEntryJson> packed = new(members.Count, StringComparer.Ordinal);
        Dictionary<string, TextureEntryJson> pageFacts = new(StringComparer.Ordinal);
        int first = 0;

        foreach (IGrouping<(TextureFormatSetting Format, TextureSamplingSetting Sampling), string> group in members
            .GroupBy(key => (settings[key].Format, settings[key].Sampling))
            .OrderBy(static group => group.Key.Format)
            .ThenBy(static group => group.Key.Sampling))
        {
            (Placement[] placements, (int Width, int Height)[] extents) = AtlasPacker.Pack(
                [.. group.Select(key => (key, images[key].Width + (2 * Extrude), images[key].Height + (2 * Extrude)))],
                maxSize);
            int channels = TexturePixels.Channels(group.Key.Format);
            byte[][] pages = [.. extents.Select(extent => new byte[extent.Width * extent.Height * channels])];

            foreach (Placement placement in placements)
            {
                int x = placement.X + Extrude;
                int y = placement.Y + Extrude;
                TexturePixels.Blit(pages[placement.Page], extents[placement.Page].Width, images[placement.Key], x, y, Extrude);
                packed.Add(placement.Key, new TextureEntryJson { Page = PageName(atlas, first + placement.Page), X = x, Y = y });
            }

            for (int page = 0; page < pages.Length; page++)
            {
                string name = PageName(atlas, first + page);
                if (TextureEntryJson.Facts(group.Key.Format, group.Key.Sampling) is { } facts)
                {
                    pageFacts.Add(name, facts);
                }

                byte[] texels = pages[page];
                (int width, int height) = extents[page];
                AtomicFile.Write(PagePath(pass.Shipped, name), path =>
                {
                    using FileStream file = File.Create(path);
                    TexturePixels.Encode(texels, width, height, file, channels);
                });
            }

            first += pages.Length;
        }

        return new TextureMapJson { Textures = packed, Pages = pageFacts };
    }
}

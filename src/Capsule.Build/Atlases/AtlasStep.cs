using System.Globalization;
using Capsule.Assets;
using Capsule.Build.Caching;
using Capsule.Build.Textures;

namespace Capsule.Build.Atlases;

/// <summary>
/// Packs every texture whose <c>atlas</c> setting names a declared atlas onto that atlas's pages, and
/// sets <see cref="PipelinePass.TextureMap"/> holding every packed texture's page and offset and every
/// page's non-default facts.
/// </summary>
internal static class AtlasStep
{
    private const string Step = "atlases";

    /// <summary>Texels of border duplicated outward on every side of a member.</summary>
    internal const int Extrude = 1;

    // Where every page ships below assets/, as atlases/game.0.png. No key holds a '.', so no asset
    // ships at a page's path.
    private const string PageDirectory = "atlases/";

    internal static void Run(PipelinePass pass)
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

        // An undeclared atlas or a defective atlas file already failed the build.
        List<(string Name, AtlasConfigJson Config, string Path, List<string> Members)> atlases = [];
        foreach ((string atlas, List<string> members) in packing)
        {
            if (pass.Atlases.TryGetValue(atlas, out DeclaredAtlas declared) && declared.Config is { } config)
            {
                members.Sort(StringComparer.Ordinal);
                atlases.Add((atlas, config, declared.Path, members));
            }
        }

        // An atlas derives from its own settings and every member's texels, format and sampling.
        foreach ((_, TextureMapJson packed) in pass.Each(
            Step,
            atlases,
            atlas => new Derivation(
                atlas.Path,
                [.. atlas.Members.Select(key => pass.Textures[key].Path)],
                Settings(atlas.Name, MaxSize(atlas.Config), atlas.Members, pass.TextureSettings),
                typeof(AtlasStep).Assembly),
            (atlas, files) => Repack(pass, atlas.Name, MaxSize(atlas.Config), atlas.Path, atlas.Members, files),
            TextureMapJsonContext.Default.TextureMapJson))
        {
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

    private static int MaxSize(AtlasConfigJson config) => config.MaxSize ?? AtlasConfigJson.DefaultMaxSize;

    private static string PageName(string atlas, int page) =>
        $"{PageDirectory}{atlas}.{page.ToString(CultureInfo.InvariantCulture)}";

    // The atlas's name and page size, then each member's key, format and sampling in key order.
    private static string Settings(string atlas, int maxSize, List<string> members, IReadOnlyDictionary<string, ResolvedTexture> settings) =>
        $"name={atlas}; maxSize={maxSize.ToString(CultureInfo.InvariantCulture)}; members="
            + string.Join(", ", members.Select(key => $"{key} {settings[key].Format} {settings[key].Sampling}"));

    // Decodes, packs and writes every page of one atlas, one run of pages per format and sampling with
    // the defaults first. Reports every member that cannot be packed, which fails the atlas.
    private static TextureMapJson Repack(
        PipelinePass pass,
        string atlas,
        int maxSize,
        string atlasPath,
        List<string> members,
        DerivedFiles files)
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
            return new TextureMapJson();
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
                files.Write(name + ".png", path =>
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

using System.Globalization;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Build.Configuration;
using Capsule.Build.Textures;
using StbImageSharp;
using StbImageWriteSharp;

namespace Capsule.Build.Atlases;

/// <summary>The atlas pass, which packs every texture whose <c>atlas</c> setting names a declared atlas onto that atlas's pages.</summary>
internal static class AtlasStep
{
    /// <summary>Texels of border duplicated outward on every side of a member.</summary>
    internal const int Extrude = 1;

    // Where every page ships below assets/, as atlases/game.0.png. No key holds a '.', so no asset
    // ships at a page's path.
    private const string PageDirectory = "atlases/";

    private const string StampDirectory = "atlases";

    /// <returns>A map holding every packed texture's page and offset and every page's non-default facts.</returns>
    /// <param name="settings">Every texture's resolved settings. A font's page resolves to no atlas.</param>
    /// <param name="atlases">Every declared atlas. One whose file is defective is not packed.</param>
    internal static TextureMapJson Pack(
        BuildPass pass,
        Dictionary<string, TextureConfigJson> settings,
        Dictionary<string, (string Path, AtlasConfigJson? Config)> atlases)
    {
        SortedDictionary<string, TextureEntryJson> textures = new(StringComparer.Ordinal);
        SortedDictionary<string, TextureEntryJson> pages = new(StringComparer.Ordinal);
        TextureMapJson map = new() { Textures = textures, Pages = pages };

        SortedDictionary<string, List<string>> packing = new(StringComparer.Ordinal);
        foreach ((string key, TextureConfigJson texture) in settings)
        {
            if (texture.Atlas!.Value.Name is { } atlas)
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
            return map;
        }

        Dictionary<string, string> paths = pass.Of(AssetType.Textures)
            .ToDictionary(static texture => texture.Key, static texture => texture.Path, StringComparer.Ordinal);
        string stamps = Path.Combine(pass.OutputDirectory, StampDirectory);
        Directory.CreateDirectory(stamps);
        TextureMapJson previous = Read(Path.Combine(pass.Shipped.Root, TextureMapJson.ShippedPath));

        foreach ((string atlas, List<string> members) in packing)
        {
            // An undeclared atlas or a defective atlas file already failed the build.
            if (!atlases.TryGetValue(atlas, out (string Path, AtlasConfigJson? Config) declared) || declared.Config is not { } config)
            {
                continue;
            }

            // A build that changed nothing the stamp covers reuses the last map's placements.
            int maxSize = config.MaxSize ?? AtlasConfigJson.DefaultMaxSize;
            members.Sort(StringComparer.Ordinal);
            string stamp = Stamp(maxSize, members, settings, paths);
            string stampPath = Path.Combine(stamps, atlas + ".stamp");
            TextureMapJson? packed = File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp
                ? Previous(previous, atlas, pass.Shipped)
                : null;

            if (packed is null)
            {
                packed = Repack(pass, atlas, maxSize, declared.Path, members, settings, paths);
                if (packed is null)
                {
                    continue;
                }

                AtomicFile.WriteText(stampPath, stamp);
                pass.Output.WriteLine($"atlas {atlas}: {members.Count} texture(s) packed on {packed.Textures!.Values.DistinctBy(static entry => entry.Page).Count()} page(s)");
            }
            else
            {
                pass.Output.WriteLine($"atlas {atlas}: up to date");
            }

            foreach ((string key, TextureEntryJson entry) in packed.Textures!)
            {
                textures.Add(key, entry);
            }

            foreach ((string page, TextureEntryJson facts) in packed.Pages!)
            {
                pages.Add(page, facts);
            }
        }

        return map;
    }

    private static string PageName(string atlas, int page) =>
        $"{PageDirectory}{atlas}.{page.ToString(CultureInfo.InvariantCulture)}";

    private static string PagePath(ShippedFiles shipped, string page) =>
        shipped.Claim(page + ".png", $"the atlas page \"{page}\"");

    // The map at path, or an empty one when none is there or it cannot be read.
    private static TextureMapJson Read(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);

            return JsonSerializer.Deserialize(file, TextureMapJsonContext.Default.TextureMapJson) ?? new TextureMapJson();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
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

    // A repack is decided by the atlas's own settings and every member's key, length, write time, format
    // and sampling. Reading and hashing every texture on every build costs more than the repack it saves.
    private static string Stamp(int maxSize, List<string> members, Dictionary<string, TextureConfigJson> settings, Dictionary<string, string> paths)
    {
        StringBuilder stamp = new StringBuilder("maxSize|").AppendLine(maxSize.ToString(CultureInfo.InvariantCulture));
        foreach (string key in members)
        {
            FileInfo file = new(paths[key]);
            stamp.Append(key).Append('|').Append(file.Length)
                .Append('|').Append(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(settings[key].Format!.Value.ToString())
                .Append('|').AppendLine(settings[key].Sampling!.Value.ToString());
        }

        return stamp.ToString();
    }

    // Decodes, packs and writes every page of one atlas, one run of pages per format and sampling with
    // the defaults first.
    // Reports every member that cannot be packed before returning null.
    private static TextureMapJson? Repack(
        BuildPass pass,
        string atlas,
        int maxSize,
        string atlasPath,
        List<string> members,
        Dictionary<string, TextureConfigJson> settings,
        Dictionary<string, string> paths)
    {
        Dictionary<string, Texels> images = new(members.Count, StringComparer.Ordinal);
        bool valid = true;

        foreach (string key in members)
        {
            Texels image;
            try
            {
                image = Decode(paths[key], settings[key].Format!.Value);
            }
            catch (FormatException ex)
            {
                pass.Fail(paths[key], ex.Message);
                valid = false;
                continue;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or IOException or UnauthorizedAccessException)
            {
                pass.Fail(paths[key], $"is not a PNG the packer can decode: {ex.Message}");
                valid = false;
                continue;
            }

            if (image.Width + (2 * Extrude) > maxSize || image.Height + (2 * Extrude) > maxSize)
            {
                pass.Fail(
                    paths[key],
                    $"is {image.Width}x{image.Height}, which with its {Extrude}-texel border exceeds the {maxSize}-texel page of the atlas \"{atlas}\". Set \"atlas\": false for it in its config, or raise \"maxSize\" in '{atlasPath}'.");
                valid = false;
                continue;
            }

            images.Add(key, image);
        }

        if (!valid)
        {
            return null;
        }

        Dictionary<string, TextureEntryJson> textures = new(members.Count, StringComparer.Ordinal);
        Dictionary<string, TextureEntryJson> pageFacts = new(StringComparer.Ordinal);
        int first = 0;

        foreach (IGrouping<(TextureFormatSetting Format, TextureSamplingSetting Sampling), string> group in members
            .GroupBy(key => (Format: settings[key].Format!.Value, Sampling: settings[key].Sampling!.Value))
            .OrderBy(static group => group.Key.Format)
            .ThenBy(static group => group.Key.Sampling))
        {
            (Placement[] placements, (int Width, int Height)[] extents) = AtlasPacker.Pack(
                [.. group.Select(key => (key, images[key].Width + (2 * Extrude), images[key].Height + (2 * Extrude)))],
                maxSize);
            int channels = TextureStep.Channels(group.Key.Format);
            byte[][] pages = [.. extents.Select(extent => new byte[extent.Width * extent.Height * channels])];

            foreach (Placement placement in placements)
            {
                int x = placement.X + Extrude;
                int y = placement.Y + Extrude;
                Blit(pages[placement.Page], extents[placement.Page].Width, images[placement.Key], x, y);
                textures.Add(placement.Key, new TextureEntryJson { Page = PageName(atlas, first + placement.Page), X = x, Y = y });
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
                    Encode(texels, width, height, file, channels);
                });
            }

            first += pages.Length;
        }

        return new TextureMapJson { Textures = textures, Pages = pageFacts };
    }

    /// <summary>Copies <paramref name="member"/> onto <paramref name="page"/> with its top-left texel at (<paramref name="x"/>, <paramref name="y"/>) and its border extruded.</summary>
    /// <param name="pageWidth">The page's width in texels, each of the member's channel count.</param>
    internal static void Blit(byte[] page, int pageWidth, Texels member, int x, int y)
    {
        // The border repeats Extrude texels outward on every side, corners included. A clamped linear
        // sample at the member's edge then reads the edge and not a neighbour.
        int width = member.Width;
        int height = member.Height;
        int texel = member.Channels;
        ReadOnlySpan<byte> source = member.Data;

        for (int row = -Extrude; row < height + Extrude; row++)
        {
            ReadOnlySpan<byte> line = source.Slice(Math.Clamp(row, 0, height - 1) * width * texel, width * texel);
            Span<byte> target = page.AsSpan((((y + row) * pageWidth) + x - Extrude) * texel);

            for (int i = 0; i < Extrude; i++)
            {
                line[..texel].CopyTo(target.Slice(i * texel, texel));
                line[^texel..].CopyTo(target.Slice((Extrude + width + i) * texel, texel));
            }

            line.CopyTo(target.Slice(Extrude * texel, width * texel));
        }
    }

    /// <summary>Encodes texels as a PNG into <paramref name="destination"/>, straight-alpha RGBA8 at four channels and 8-bit greyscale at one.</summary>
    internal static void Encode(byte[] texels, int width, int height, Stream destination, int channels = 4) =>
        new ImageWriter().WritePng(
            texels,
            width,
            height,
            channels == 1 ? StbImageWriteSharp.ColorComponents.Grey : StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,
            destination);

    /// <summary>A texture's texels in its format: RGBA8 as authored, or an r8 texture's one channel.</summary>
    /// <exception cref="FormatException">An r8 source is of a kind with no single channel.</exception>
    internal static Texels Decode(string path, TextureFormatSetting format)
    {
        byte[] file = File.ReadAllBytes(path);
        if (TextureStep.Channels(format) == 1)
        {
            return SingleChannelPng.Read(file);
        }

        ImageResult image = ImageResult.FromMemory(file, StbImageSharp.ColorComponents.RedGreenBlueAlpha);

        return new Texels(image.Data, image.Width, image.Height, 4);
    }
}

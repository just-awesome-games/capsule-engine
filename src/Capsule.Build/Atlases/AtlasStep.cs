using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StbImageSharp;
using StbImageWriteSharp;

namespace Capsule.Build.Atlases;

/// <summary>
/// The atlas pass. Every <c>*.atlas.json</c> claims the textures its globs match and packs them onto
/// pages, and the map the runtime reads sends each member's handle to its page and offset. Each
/// atlas is stamped with its manifest hash and every member's size and write time. A build that
/// changed nothing an atlas holds reuses the placements in the last map.
/// </summary>
internal static class AtlasStep
{
    /// <summary>Texels of border duplicated outward on every side of a member.</summary>
    internal const int Extrude = 1;

    private const string StampDirectory = "atlases";

    private const string MapFile = "atlases.json";

    private const int DefaultMaxSize = 4096;

    private const int LargestMaxSize = 8192;

    /// <returns>Every texture key an atlas packed. Those textures do not ship on their own.</returns>
    /// <param name="pages">The texture keys a font names as its pages. No atlas packs one.</param>
    internal static HashSet<string> Pack(BuildPass pass, HashSet<string> pages)
    {
        HashSet<string> packed = new(StringComparer.Ordinal);
        List<Source> atlases = [.. pass.Of(AssetType.Atlases)];
        if (atlases.Count == 0)
        {
            return packed;
        }

        Dictionary<string, string> textures = pass.Of(AssetType.Textures)
            .Where(texture => !pages.Contains(texture.Key))
            .ToDictionary(static texture => texture.Key, static texture => texture.Path, StringComparer.Ordinal);

        string stamps = Path.Combine(pass.OutputDirectory, StampDirectory);
        Directory.CreateDirectory(stamps);
        string mapPath = pass.Shipped.Claim(MapFile);
        IDictionary<string, AtlasEntryJson> previous = ReadMap(mapPath);

        // Which manifest packs each texture. A texture two manifests match is reported against
        // both.
        Dictionary<string, string> claimedBy = new(StringComparer.Ordinal);
        SortedDictionary<string, AtlasEntryJson> map = new(StringComparer.Ordinal);
        int failures = pass.Failures;

        foreach (Source atlas in atlases)
        {
            if (!TryClaim(atlas, textures, claimedBy, pass.Error, out int maxSize, out byte[] manifestBytes, out List<string> members))
            {
                pass.Failures++;
                continue;
            }

            string stamp = Stamp(manifestBytes, members, textures);
            string stampPath = Path.Combine(stamps, atlas.Key + ".atlas.stamp");
            Dictionary<string, AtlasEntryJson>? entries = File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp
                ? Previous(previous, atlas.Key, pass.Shipped)
                : null;

            if (entries is null)
            {
                entries = Repack(atlas, maxSize, members, textures, pass.Shipped, pass.Error);
                if (entries is null)
                {
                    pass.Failures++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
                AtomicFile.WriteText(stampPath, stamp);
                pass.Output.WriteLine($"atlas {atlas.Key}: {members.Count} texture(s) packed on {entries.Values.DistinctBy(static entry => entry.Page).Count()} page(s)");
            }
            else
            {
                pass.Output.WriteLine($"atlas {atlas.Key}: up to date");
            }

            foreach ((string key, AtlasEntryJson entry) in entries)
            {
                map.Add(key, entry);
                packed.Add(key);
            }
        }

        if (pass.Failures == failures)
        {
            AtomicFile.Write(mapPath, path =>
            {
                using FileStream file = File.Create(path);
                JsonSerializer.Serialize(file, new AtlasMapJson { Textures = map }, AtlasMapJsonContext.Default.AtlasMapJson);
            });
        }

        return packed;
    }

    /// <summary>Parses a manifest into its globs, each compiled over keys, and its page extent.</summary>
    /// <exception cref="FormatException">The JSON is malformed or the document breaks the format.</exception>
    internal static ((string Pattern, Regex Match)[] Patterns, int MaxSize) ReadManifest(string text)
    {
        AtlasManifestJson? raw;
        try
        {
            raw = JsonSerializer.Deserialize(text, AtlasManifestJsonContext.Default.AtlasManifestJson);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"is not a valid atlas manifest: {ex.Message}", ex);
        }

        if (raw?.Textures is not { Length: > 0 } patterns)
        {
            throw new FormatException("declares no textures. \"textures\" is a non-empty array of globs over texture keys.");
        }

        int maxSize = raw.MaxSize ?? DefaultMaxSize;
        if (maxSize <= 0 || maxSize > LargestMaxSize || !int.IsPow2(maxSize))
        {
            throw new FormatException($"declares a maxSize of {maxSize}. A page extent is a power of two no larger than {LargestMaxSize}.");
        }

        return ([.. patterns.Select(static pattern => (pattern ?? string.Empty, Glob(pattern)))], maxSize);
    }

    // A glob over keys as one anchored expression. '*' matches any run within a segment. '**' at the
    // end matches everything below that directory, and in the middle matches any depth, including
    // none.
    private static Regex Glob(string? pattern)
    {
        string[] segments = pattern?.Split('/') ?? [];
        if (segments.Length == 0 || segments.Any(static segment => segment != "**" && (segment.Contains("**", StringComparison.Ordinal) || !Regex.IsMatch(segment, "^[A-Za-z0-9_*-]+$"))))
        {
            throw new FormatException($"lists the texture pattern \"{pattern}\". A pattern is forward-slash segments of key characters and '*', or '**' alone.");
        }

        StringBuilder expression = new("^");
        for (int i = 0; i < segments.Length; i++)
        {
            bool last = i == segments.Length - 1;
            if (segments[i] == "**")
            {
                expression.Append(last ? (i == 0 ? ".+" : "/.+") : (i == 0 ? "(?:.*/)?" : "/(?:.*/)?"));
            }
            else
            {
                if (i > 0 && segments[i - 1] != "**")
                {
                    expression.Append('/');
                }

                expression.Append(Regex.Escape(segments[i]).Replace(@"\*", "[^/]*", StringComparison.Ordinal));
            }
        }

        return new Regex(expression.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    // A page ships beside its manifest. No key holds a '.', so no texture ships at a page's path.
    private static string PagePath(ShippedFiles shipped, string page) =>
        shipped.Claim(page + ".png");

    private static IDictionary<string, AtlasEntryJson> ReadMap(string mapPath)
    {
        try
        {
            using FileStream file = File.OpenRead(mapPath);

            return JsonSerializer.Deserialize(file, AtlasMapJsonContext.Default.AtlasMapJson)?.Textures ?? new Dictionary<string, AtlasEntryJson>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, AtlasEntryJson>();
        }
    }

    // The atlas's placements from the last map written, or null when the map lacks the atlas or one
    // of its pages is gone. Either means it must be packed again.
    private static Dictionary<string, AtlasEntryJson>? Previous(IDictionary<string, AtlasEntryJson> previous, string atlas, ShippedFiles shipped)
    {
        Dictionary<string, AtlasEntryJson> entries = new(StringComparer.Ordinal);
        foreach ((string key, AtlasEntryJson entry) in previous)
        {
            if (entry.Page is { } page && page.StartsWith(atlas + ".", StringComparison.Ordinal))
            {
                if (!File.Exists(PagePath(shipped, page)))
                {
                    return null;
                }

                entries.Add(key, entry);
            }
        }

        return entries.Count > 0 ? entries : null;
    }

    // Reads one manifest and resolves its globs against the texture set. Reports every defect
    // before returning false.
    private static bool TryClaim(
        Source atlas,
        Dictionary<string, string> textures,
        Dictionary<string, string> claimedBy,
        TextWriter error,
        out int maxSize,
        out byte[] manifestBytes,
        out List<string> members)
    {
        members = [];
        maxSize = 0;
        manifestBytes = [];
        (string Pattern, Regex Match)[] patterns;

        try
        {
            manifestBytes = File.ReadAllBytes(atlas.Path);
            (patterns, maxSize) = ReadManifest(Encoding.UTF8.GetString(manifestBytes).TrimStart('\uFEFF'));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{atlas.Path}: {ex.Message}");

            return false;
        }

        bool valid = true;
        HashSet<string> matched = new(StringComparer.Ordinal);
        foreach ((string pattern, Regex match) in patterns)
        {
            int hits = 0;
            foreach (string key in textures.Keys)
            {
                if (match.IsMatch(key))
                {
                    hits++;
                    matched.Add(key);
                }
            }

            if (hits == 0)
            {
                error.WriteLine($"{atlas.Path}: the texture pattern \"{pattern}\" matches no texture key. Every pattern must pack at least one texture.");
                valid = false;
            }
        }

        foreach (string key in matched.Order(StringComparer.Ordinal))
        {
            if (claimedBy.TryGetValue(key, out string? claimant))
            {
                error.WriteLine($"{atlas.Path}: packs '{textures[key]}', which '{claimant}' already packs. A texture belongs to one atlas.");
                valid = false;
                continue;
            }

            claimedBy.Add(key, atlas.Path);
            members.Add(key);
        }

        return valid;
    }

    // A repack is decided by the manifest itself plus every member's key, length and write time.
    // Reading and hashing every texture on every build costs more than the repack it saves.
    private static string Stamp(byte[] manifestBytes, List<string> members, Dictionary<string, string> textures)
    {
        StringBuilder stamp = new();
        stamp.AppendLine(Convert.ToHexString(SHA256.HashData(manifestBytes)));

        foreach (string key in members)
        {
            FileInfo file = new(textures[key]);
            stamp.Append(key).Append('|').Append(file.Length)
                .Append('|')
                .AppendLine(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        return stamp.ToString();
    }

    // Decodes, packs and writes every page of one atlas. Reports every member that cannot be packed
    // before returning null.
    private static Dictionary<string, AtlasEntryJson>? Repack(
        Source atlas,
        int maxSize,
        List<string> members,
        Dictionary<string, string> textures,
        ShippedFiles shipped,
        TextWriter error)
    {
        Dictionary<string, ImageResult> images = new(members.Count, StringComparer.Ordinal);
        List<(string Key, int Width, int Height)> items = new(members.Count);
        bool valid = true;

        foreach (string key in members)
        {
            ImageResult image;
            try
            {
                image = ImageResult.FromMemory(File.ReadAllBytes(textures[key]), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"{textures[key]}: is not a PNG the packer can decode: {ex.Message}");
                valid = false;
                continue;
            }

            if (image.Width + (2 * Extrude) > maxSize || image.Height + (2 * Extrude) > maxSize)
            {
                error.WriteLine(
                    $"{textures[key]}: is {image.Width}x{image.Height}, which with its {Extrude}-texel border exceeds the {maxSize}-texel page '{atlas.Path}' declares. Raise maxSize or leave the texture out of the atlas.");
                valid = false;
                continue;
            }

            images.Add(key, image);
            items.Add((key, image.Width + (2 * Extrude), image.Height + (2 * Extrude)));
        }

        if (!valid)
        {
            return null;
        }

        (Placement[] placements, (int Width, int Height)[] extents) = AtlasPacker.Pack(items, maxSize);
        byte[][] pages = [.. extents.Select(static extent => new byte[extent.Width * extent.Height * 4])];
        Dictionary<string, AtlasEntryJson> entries = new(placements.Length, StringComparer.Ordinal);

        foreach (Placement placement in placements)
        {
            int x = placement.X + Extrude;
            int y = placement.Y + Extrude;
            Blit(pages[placement.Page], extents[placement.Page].Width, images[placement.Key], x, y);
            entries.Add(placement.Key, new AtlasEntryJson { Page = $"{atlas.Key}.{placement.Page.ToString(CultureInfo.InvariantCulture)}", X = x, Y = y });
        }

        for (int page = 0; page < pages.Length; page++)
        {
            string pagePath = PagePath(shipped, $"{atlas.Key}.{page.ToString(CultureInfo.InvariantCulture)}");
            byte[] texels = pages[page];
            (int width, int height) = extents[page];
            AtomicFile.Write(pagePath, path =>
            {
                using FileStream file = File.Create(path);
                Encode(texels, width, height, file);
            });
        }

        return entries;
    }

    /// <summary>
    /// Copies <paramref name="member"/> onto <paramref name="page"/> (RGBA8, <paramref name="pageWidth"/>
    /// texels a row) with its top-left texel at (<paramref name="x"/>, <paramref name="y"/>). The
    /// border is extruded <see cref="Extrude"/> texels outward on every side, corners included. A
    /// clamped linear sample at the member's edge then reads the edge and not a neighbour.
    /// </summary>
    internal static void Blit(byte[] page, int pageWidth, ImageResult member, int x, int y)
    {
        int width = member.Width;
        int height = member.Height;
        ReadOnlySpan<byte> source = member.Data;

        for (int row = -Extrude; row < height + Extrude; row++)
        {
            ReadOnlySpan<byte> line = source.Slice(Math.Clamp(row, 0, height - 1) * width * 4, width * 4);
            Span<byte> target = page.AsSpan((((y + row) * pageWidth) + x - Extrude) * 4);

            for (int i = 0; i < Extrude; i++)
            {
                line[..4].CopyTo(target.Slice(i * 4, 4));
                line[^4..].CopyTo(target.Slice((Extrude + width + i) * 4, 4));
            }

            line.CopyTo(target.Slice(Extrude * 4, width * 4));
        }
    }

    /// <summary>Encodes straight-alpha RGBA8 texels as a PNG into <paramref name="destination"/>.</summary>
    internal static void Encode(byte[] texels, int width, int height, Stream destination) =>
        new ImageWriter().WritePng(texels, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, destination);
}

internal sealed class AtlasManifestJson
{
    [JsonPropertyName("textures")]
    public string?[]? Textures { get; set; }

    [JsonPropertyName("maxSize")]
    public int? MaxSize { get; set; }
}

/// <summary>The shipped map: each packed texture key to the page it lives on and the position of its texel (0, 0).</summary>
internal sealed class AtlasMapJson
{
    [JsonPropertyName("textures")]
    public IDictionary<string, AtlasEntryJson>? Textures { get; set; }
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

// Reflection-based serialization is off solution-wide. Disallow makes an unknown manifest member a
// defect instead of an ignored typo.
[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AtlasManifestJson))]
internal sealed partial class AtlasManifestJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AtlasMapJson))]
internal sealed partial class AtlasMapJsonContext : JsonSerializerContext;

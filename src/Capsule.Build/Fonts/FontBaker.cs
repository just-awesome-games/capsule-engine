using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Build.Configuration;
using Capsule.Build.Textures;

namespace Capsule.Build.Fonts;

/// <summary>
/// Bakes a <c>&lt;name&gt;.font.json</c> into the text <c>.fnt</c> and pages a bitmap font is authored as,
/// which the font step then reads as it reads an authored one.
/// </summary>
/// <remarks>
/// Every font bakes printable ASCII, Latin-1, the replacement character, and unless it opts out every
/// character any string table uses. A font cannot know which labels draw it. Its pages are one-channel
/// <c>r8</c> textures, sampled as their folder's settings say.
/// </remarks>
internal sealed class FontBaker(int[] tableCodepoints) : IAssetImporter
{
    // What every font bakes that its faces carry: printable ASCII, from the space to the tilde, Latin-1, from
    // the no-break space to ÿ, whose accented letters the pseudo-language draws, and the replacement
    // character. A font lacking the replacement draws its question mark instead.
    private static readonly int[] Always = [.. Range(32, 126), .. Range(160, 255), '�'];

    // The texture settings written beside each page. A page holds only coverage, and stores one channel.
    private const string OneChannelPage = """{ "format": "r8" }""";

    public IReadOnlyList<string> Extensions { get; } = [AssetType.BakedFont];

    /// <summary>What a bake reads besides its files, the characters of the string tables, for its derivation's stamp.</summary>
    internal string Stamp { get; } = Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(tableCodepoints.AsSpan())));

    /// <summary>Every character the string tables at <paramref name="paths"/> use, ascending with no repeat.</summary>
    /// <remarks>A table that is not valid JSON adds nothing, and the strings step reports it.</remarks>
    internal static int[] TableCodepoints(IEnumerable<string> paths)
    {
        SortedSet<int> codepoints = [];
        foreach (string path in paths)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name != SchemaKeyConverter.Key)
                        {
                            Collect(property.Value, codepoints);
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return [.. codepoints];
    }

    public void Import(AssetImportContext context)
    {
        FontBakeJson font = Read(context.ReadAllBytes(context.SourcePath));
        if (font.Face is not { Length: > 0 } face)
        {
            throw new FormatException($"names no face. {FontBakeJson.Shape}");
        }

        if (font.Size is not { } size)
        {
            throw new FormatException($"gives no size. {FontBakeJson.Shape}");
        }

        if (size is < 1 or > FontBakeJson.LargestSize)
        {
            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"gives size {size}. Give an em from 1 to {FontBakeJson.LargestSize} pixels."));
        }

        string folder = Path.GetDirectoryName(context.SourcePath) ?? string.Empty;
        List<(string Name, byte[] Data)> faces = [];
        foreach (string? named in (IEnumerable<string?>)[face, .. font.Fallback ?? []])
        {
            if (named is not { Length: > 0 } || named.Any(char.IsControl) || Path.IsPathRooted(named)
                || !(named.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || named.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
            {
                throw new FormatException($"names the face \"{named}\". Name each face as a .ttf or .otf file relative to this one.");
            }

            string path = Path.Combine(folder, named).Replace('\\', '/');
            faces.Add(context.Exists(path)
                ? (named, context.ReadAllBytes(path))
                : throw new FormatException($"names the face \"{named}\", and no file is at \"{path}\". Name a .ttf or .otf file relative to this one."));
        }

        SortedSet<int> codepoints = [.. Always];
        if (font.Tables ?? true)
        {
            codepoints.UnionWith(tableCodepoints);
        }

        foreach (Rune rune in (font.Characters ?? string.Empty).EnumerateRunes())
        {
            codepoints.Add(rune.Value);
        }

        codepoints.RemoveWhere(static codepoint => !Drawn(codepoint));
        FontRasterizer.Baked baked = FontRasterizer.Bake(faces, size, font.Antialias ?? true, [.. codepoints]);

        string stem = context.AssetPath[..^AssetType.BakedFont.Length];
        string name = Path.GetFileName(stem);
        string[] pages = [.. baked.Pages.Select((_, page) => page == 0 ? name + ".png" : string.Create(CultureInfo.InvariantCulture, $"{name}_{page}.png"))];
        string pageFolder = stem[..^name.Length];
        for (int page = 0; page < pages.Length; page++)
        {
            using MemoryStream png = new();
            PngWriter.Write(baked.Pages[page], baked.PageWidth, baked.PageHeight, channels: 1, png);
            context.Write(pageFolder + pages[page], png.ToArray());
            context.Write(pageFolder + pages[page] + ".config.json", OneChannelPage);
        }

        context.Write(stem + ".fnt", Description(face, size, baked, pages));
    }

    // Every codepoint from first to last, both included.
    private static IEnumerable<int> Range(int first, int last) => Enumerable.Range(first, last - first + 1);

    private static FontBakeJson Read(byte[] json) =>
        StrictJson.Read(json, FontBakeJsonContext.Default.FontBakeJson, FontBakeJson.Members, nullFix: null, FontBakeJson.Shape);

    // Whether a codepoint draws a glyph: no control or format character, which a run passes over.
    private static bool Drawn(int codepoint) =>
        Rune.IsValid(codepoint)
        && Rune.GetUnicodeCategory(new Rune(codepoint)) is not (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate);

    // The font as BMFont's text flavour spells it.
    private static string Description(string face, int size, FontRasterizer.Baked baked, string[] pages)
    {
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"info face=\"{face}\" size={size} unicode=1\n");
        text.Append(CultureInfo.InvariantCulture, $"common lineHeight={baked.LineHeight} base={baked.Baseline} scaleW={baked.PageWidth} scaleH={baked.PageHeight} pages={pages.Length} packed=0\n");
        for (int page = 0; page < pages.Length; page++)
        {
            text.Append(CultureInfo.InvariantCulture, $"page id={page} file=\"{pages[page]}\"\n");
        }

        foreach (BmGlyph glyph in baked.Glyphs)
        {
            text.Append(CultureInfo.InvariantCulture, $"char id={glyph.Codepoint} x={glyph.X} y={glyph.Y} width={glyph.Width} height={glyph.Height} xoffset={glyph.XOffset} yoffset={glyph.YOffset} xadvance={glyph.XAdvance} page={glyph.Page}\n");
        }

        foreach (BmKerning kerning in baked.Kernings)
        {
            text.Append(CultureInfo.InvariantCulture, $"kerning first={kerning.First} second={kerning.Second} amount={kerning.Amount}\n");
        }

        return text.ToString();
    }

    // Every character of every string value below element, keys and structure aside.
    private static void Collect(JsonElement element, SortedSet<int> codepoints)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Collect(property.Value, codepoints);
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Collect(item, codepoints);
                }

                break;
            case JsonValueKind.String:
                foreach (Rune rune in element.GetString()!.EnumerateRunes())
                {
                    codepoints.Add(rune.Value);
                }

                break;
            default:
                break;
        }
    }
}

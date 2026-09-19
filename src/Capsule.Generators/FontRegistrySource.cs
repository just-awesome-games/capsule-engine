using System.Globalization;
using System.Text;
using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

// One shipped font, with every page resolved to the key and extension the build ships it at.
internal sealed class FontSource(string key, BmFontDescription description, string[] pageKeys, string[] pageExtensions)
{
    internal string Key { get; } = key;

    internal BmFontDescription Description { get; } = description;

    internal string[] PageKeys { get; } = pageKeys;

    internal string[] PageExtensions { get; } = pageExtensions;
}

// Renders every font a game ships as typed members of CapsuleAssets.Fonts, each carrying the
// metrics, glyphs and kerning read out of its '.fnt'. A misspelt font is a compile error and
// nothing is parsed at run time.
internal static class FontRegistrySource
{
    internal const string RegistryClass = "Fonts";

    internal const string Domain = "fonts";

    private const string FontType = "global::Capsule.Rendering.BitmapFont";

    private const string HandleType = "global::Capsule.Assets.TextureHandle";

    private const string GlyphType = "global::Capsule.Rendering.Glyph";

    private const string RegionType = "global::Capsule.Rendering.TextureRegion";

    private const string KerningType = "global::Capsule.Rendering.KerningPair";

    /// <summary>Reads one <c>.fnt</c> additional file into the model the registry is built from.</summary>
    internal static ParsedAsset<BmFontDescription> Describe(
        AdditionalText text,
        string authored,
        CancellationToken cancellation) =>
        ParsedAsset.Describe<BmFontDescription>(
            text,
            authored,
            Domain,
            BmFontParser.BmFontExtension,
            "cannot be read as text. Export the font as text.",
            BmFontParser.Parse,
            cancellation);

    /// <summary>
    /// Resolves every page the font names to the shipped asset that carries it. Page keys are
    /// derived the same way the key pass derives them, so the two agree across normalization.
    /// </summary>
    /// <returns>The source, or null with <paramref name="error"/> set.</returns>
    internal static FontSource? Resolve(
        string key,
        BmFontDescription description,
        IReadOnlyDictionary<string, string> shipped,
        out string? error)
    {
        int slash = key.LastIndexOf('/');
        string directory = slash < 0 ? string.Empty : key.Substring(0, slash + 1);
        string[] keys = new string[description.PageFiles.Length];
        string[] extensions = new string[description.PageFiles.Length];

        for (int i = 0; i < description.PageFiles.Length; i++)
        {
            string file = description.PageFiles[i];
            string stem = file.Substring(0, file.Length - Path.GetExtension(file).Length);

            if (TypeNaming.NormalizeKey(stem, out string? rejected) is not { } normalized)
            {
                error = $"names page \"{file}\", whose \"{rejected}\" is no C# name. A path segment under a domain root holds letters, digits, '-' and '_', and does not start with a digit.";

                return null;
            }

            string page = directory + normalized;

            // The handle names the file the build wrote, not the spelling the font used.
            if (!shipped.TryGetValue(page, out string? extension))
            {
                error = $"names page \"{file}\", and this game ships nothing at \"assets/fonts/{page}\". Author the page under Assets/Fonts/ and keep it out of a development-only directory.";

                return null;
            }

            keys[i] = page;
            extensions[i] = extension;
        }

        error = null;

        return new FontSource(key, description, keys, extensions);
    }

    internal static RegistryDomain<FontSource> Registry() =>
        new(
            RegistryClass,
            Domain,
            FontType,
            "font",
            "Every font this game ships, with the glyphs the build read for it.",
            AppendFont);

    // A font is a class, so the member hands back a single instance built once from literal data
    // instead of rebuilding its tables per call.
    private static void AppendFont(StringBuilder source, string indent, string identifier, FontSource font)
    {
        BmFontDescription described = font.Description;
        string field = "_font" + identifier;
        string body = indent + "    ";

        source.Append(indent).Append("private static readonly ").Append(FontType).Append(' ').Append(field)
            .Append(" = new ").Append(FontType).AppendLine("(");
        source.Append(body).Append(Number(described.LineHeight)).AppendLine(",");
        source.Append(body).Append(Number(described.Base)).AppendLine(",");

        source.Append(body).Append("new ").Append(HandleType).AppendLine("[]");
        source.Append(body).AppendLine("{");
        for (int i = 0; i < font.PageKeys.Length; i++)
        {
            source.Append(body).Append("    ").Append(HandleType).Append(".FontPage(").Append(Literal(font.PageKeys[i]))
                .Append(", ").Append(Literal(font.PageExtensions[i])).AppendLine("),");
        }

        source.Append(body).AppendLine("},");

        source.Append(body).Append("new ").Append(GlyphType).AppendLine("[]");
        source.Append(body).AppendLine("{");
        foreach (BmGlyph glyph in described.Glyphs)
        {
            source.Append(body).Append("    new ").Append(GlyphType).Append('(').Append(Number(glyph.Codepoint))
                .Append(", ").Append(Number(glyph.Page)).Append(", new ").Append(RegionType).Append('(')
                .Append(Number(glyph.X)).Append(", ").Append(Number(glyph.Y)).Append(", ")
                .Append(Number(glyph.Width)).Append(", ").Append(Number(glyph.Height)).Append("), ")
                .Append(Number(glyph.XOffset)).Append(", ").Append(Number(glyph.YOffset)).Append(", ")
                .Append(Number(glyph.XAdvance)).AppendLine("),");
        }

        source.Append(body).AppendLine("},");

        source.Append(body).Append("new ").Append(KerningType).AppendLine("[]");
        source.Append(body).AppendLine("{");
        foreach (BmKerning kerning in described.Kernings)
        {
            source.Append(body).Append("    new ").Append(KerningType).Append('(').Append(Number(kerning.First))
                .Append(", ").Append(Number(kerning.Second)).Append(", ").Append(Number(kerning.Amount))
                .AppendLine("),");
        }

        source.Append(body).AppendLine("});");
        source.AppendLine();

        source.Append(indent).Append("/// <summary><c>").Append(Domain).Append('/').Append(font.Key)
            .Append(BmFontParser.BmFontExtension).Append("</c>, ").Append(Number(described.LineHeight))
            .Append(" pixels per line, ").Append(Number(described.Glyphs.Length)).Append(" glyph(s) over ")
            .Append(Number(font.PageKeys.Length)).AppendLine(" page(s).</summary>");
        source.Append(indent).Append("public static ").Append(FontType).Append(' ').Append(identifier)
            .Append(" => ").Append(field).AppendLine(";");
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}

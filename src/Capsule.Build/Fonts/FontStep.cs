using System.Text;
using Capsule.Build.Registry;

namespace Capsule.Build.Fonts;

/// <summary>
/// Every bitmap font, compiled into a <c>BitmapFont</c> carrying the metrics, glyphs and kerning
/// read out of its <c>.fnt</c>. The textures it names beside it are its pages. A misspelt font is a
/// compile error and nothing is parsed at run time.
/// </summary>
internal static class FontStep
{
    private const string FontType = "global::Capsule.Rendering.BitmapFont";

    private const string HandleType = "global::Capsule.Assets.TextureHandle";

    private const string GlyphType = "global::Capsule.Rendering.Glyph";

    private const string RegionType = "global::Capsule.Rendering.TextureRegion";

    private const string KerningType = "global::Capsule.Rendering.KerningPair";

    // A byte that is no UTF-8 fails the font instead of reading as a replacement character.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <returns>The key of every texture a font names as its page.</returns>
    internal static HashSet<string> Build(BuildPass pass)
    {
        Dictionary<string, Source> textures = pass.Of(AssetType.Textures)
            .ToDictionary(static texture => texture.Key, StringComparer.Ordinal);
        HashSet<string> pages = new(StringComparer.Ordinal);

        foreach ((Source font, (BmFontDescription description, Source[] fontPages)) in pass.Each(
            pass.Of(AssetType.Fonts),
            font =>
            {
                BmFontDescription description = BmFontParser.Parse(File.ReadAllText(font.Path, StrictUtf8));

                return (description, (Source[])[.. description.PageFiles.Select(file => Page(pass, font, file, textures))]);
            }))
        {
            pages.UnionWith(fontPages.Select(static page => page.Key));
            pass.Declare(font, (source, indent, identifier) => AppendFont(source, indent, identifier, font, description, fontPages));
        }

        return pages;
    }

    // The texture a page file names, resolved beside the font.
    private static Source Page(BuildPass pass, Source font, string file, Dictionary<string, Source> textures)
    {
        string path = Path.Combine(Path.GetDirectoryName(font.Path)!, file).Replace('\\', '/');
        string below = Keys.Below(pass.Requests.AssetRoot, path);
        string subject = $"names page \"{file}\"";

        return textures.TryGetValue(Keys.Of(below[..^Path.GetExtension(below).Length], subject), out Source texture)
            ? texture
            : throw new FormatException(
                $"{subject}, and this game authors no texture at \"{path}\". Author the page beside the font and keep it out of a development-only directory.");
    }

    // A font is a class, so the member hands back a single instance built once from literal data
    // instead of rebuilding its tables per call.
    private static void AppendFont(
        StringBuilder source,
        string indent,
        string identifier,
        Source authored,
        BmFontDescription described,
        Source[] pages)
    {
        string field = "_font" + identifier;
        string body = indent + "    ";

        source.Append(indent).Append("private static readonly ").Append(FontType).Append(' ').Append(field)
            .Append(" = new ").Append(FontType).AppendLine("(");
        source.Append(body).Append(Literal.Of(described.LineHeight)).AppendLine(",");
        source.Append(body).Append(Literal.Of(described.Base)).AppendLine(",");

        source.Append(body).Append("new ").Append(HandleType).AppendLine("[]");
        source.Append(body).AppendLine("{");
        foreach (Source page in pages)
        {
            source.Append(body).Append("    new ").Append(HandleType).Append('(').Append(Literal.Of(page.Key))
                .Append(", ").Append(Literal.Of(page.Extension)).AppendLine("),");
        }

        source.Append(body).AppendLine("},");

        source.Append(body).Append("new ").Append(GlyphType).AppendLine("[]");
        source.Append(body).AppendLine("{");
        foreach (BmGlyph glyph in described.Glyphs)
        {
            source.Append(body).Append("    new ").Append(GlyphType).Append('(').Append(Literal.Of(glyph.Codepoint))
                .Append(", ").Append(Literal.Of(glyph.Page)).Append(", new ").Append(RegionType).Append('(')
                .Append(Literal.Of(glyph.X)).Append(", ").Append(Literal.Of(glyph.Y)).Append(", ")
                .Append(Literal.Of(glyph.Width)).Append(", ").Append(Literal.Of(glyph.Height)).Append("), ")
                .Append(Literal.Of(glyph.XOffset)).Append(", ").Append(Literal.Of(glyph.YOffset)).Append(", ")
                .Append(Literal.Of(glyph.XAdvance)).AppendLine("),");
        }

        source.Append(body).AppendLine("},");

        source.Append(body).Append("new ").Append(KerningType).AppendLine("[]");
        source.Append(body).AppendLine("{");
        foreach (BmKerning kerning in described.Kernings)
        {
            source.Append(body).Append("    new ").Append(KerningType).Append('(').Append(Literal.Of(kerning.First))
                .Append(", ").Append(Literal.Of(kerning.Second)).Append(", ").Append(Literal.Of(kerning.Amount))
                .AppendLine("),");
        }

        source.Append(body).AppendLine("});");
        source.AppendLine();

        source.Append(indent).Append("/// <summary><c>").Append(authored.Key)
            .Append(authored.Extension).Append("</c>, ").Append(Literal.Of(described.LineHeight)).Append(" pixels per line, ")
            .Append(Literal.Of(described.Glyphs.Length)).Append(" glyph(s) over ")
            .Append(Literal.Of(pages.Length)).AppendLine(" page(s).</summary>");
        source.Append(indent).Append("public static ").Append(FontType).Append(' ').Append(identifier)
            .Append(" => ").Append(field).AppendLine(";");
    }
}

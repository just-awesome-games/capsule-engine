using System.Text;
using Capsule.Build.Registry;

namespace Capsule.Build.Fonts;

/// <summary>Writes a font's member: a <c>BitmapFont</c> carrying the metrics, glyphs and kerning its <c>.fnt</c> declares.</summary>
internal static class FontMembers
{
    // A font is a class, so the member hands back a single instance built once from literal data
    // instead of rebuilding its tables per call.
    internal static void Write(StringBuilder code, string indent, string identifier, Source authored, (BmFontDescription Described, Source[] Pages) font)
    {
        (BmFontDescription described, Source[] pages) = font;
        string field = "_font" + identifier;
        string body = indent + "    ";

        code.Append(indent).Append("private static readonly ").Append(GeneratedTypes.BitmapFont).Append(' ').Append(field)
            .Append(" = new ").Append(GeneratedTypes.BitmapFont).AppendLine("(");
        code.Append(body).Append(Literal.Of(described.LineHeight)).AppendLine(",");
        code.Append(body).Append(Literal.Of(described.Base)).AppendLine(",");

        code.Append(body).Append("new ").Append(GeneratedTypes.TextureHandle).AppendLine("[]");
        code.Append(body).AppendLine("{");
        foreach (Source page in pages)
        {
            code.Append(body).Append("    new ").Append(GeneratedTypes.TextureHandle).Append('(').Append(Literal.Of(page.Key))
                .Append(", ").Append(Literal.Of(page.Extension)).AppendLine("),");
        }

        code.Append(body).AppendLine("},");

        code.Append(body).Append("new ").Append(GeneratedTypes.Glyph).AppendLine("[]");
        code.Append(body).AppendLine("{");
        foreach (BmGlyph glyph in described.Glyphs)
        {
            code.Append(body).Append("    new ").Append(GeneratedTypes.Glyph).Append('(').Append(Literal.Of(glyph.Codepoint))
                .Append(", ").Append(Literal.Of(glyph.Page)).Append(", new ").Append(GeneratedTypes.TextureRegion).Append('(')
                .Append(Literal.Of(glyph.X)).Append(", ").Append(Literal.Of(glyph.Y)).Append(", ")
                .Append(Literal.Of(glyph.Width)).Append(", ").Append(Literal.Of(glyph.Height)).Append("), ")
                .Append(Literal.Of(glyph.XOffset)).Append(", ").Append(Literal.Of(glyph.YOffset)).Append(", ")
                .Append(Literal.Of(glyph.XAdvance)).AppendLine("),");
        }

        code.Append(body).AppendLine("},");

        code.Append(body).Append("new ").Append(GeneratedTypes.KerningPair).AppendLine("[]");
        code.Append(body).AppendLine("{");
        foreach (BmKerning kerning in described.Kernings)
        {
            code.Append(body).Append("    new ").Append(GeneratedTypes.KerningPair).Append('(').Append(Literal.Of(kerning.First))
                .Append(", ").Append(Literal.Of(kerning.Second)).Append(", ").Append(Literal.Of(kerning.Amount))
                .AppendLine("),");
        }

        code.Append(body).AppendLine("});");
        code.AppendLine();

        code.Append(indent).Append("/// <summary><c>").Append(authored.Key)
            .Append(authored.Extension).Append("</c>, ").Append(Literal.Of(described.LineHeight)).Append(" pixels per line, ")
            .Append(Literal.Of(described.Glyphs.Length)).Append(" glyph(s) over ")
            .Append(Literal.Of(pages.Length)).AppendLine(" page(s).</summary>");
        code.Append(indent).Append("public static ").Append(GeneratedTypes.BitmapFont).Append(' ').Append(identifier)
            .Append(" => ").Append(field).AppendLine(";");
    }
}

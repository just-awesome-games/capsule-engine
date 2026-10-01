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

        ArrayOf(code, body, GeneratedTypes.TextureHandle, pages.Select(static page => $"{Literal.Of(page.Key)}, {Literal.Of(page.Extension)}"));
        code.AppendLine(",");
        ArrayOf(code, body, GeneratedTypes.Glyph, described.Glyphs.Select(static glyph =>
            $"{Literal.Of(glyph.Codepoint)}, {Literal.Of(glyph.Page)}, new {GeneratedTypes.TextureRegion}({Literal.Of(glyph.X)}, {Literal.Of(glyph.Y)}, "
                + $"{Literal.Of(glyph.Width)}, {Literal.Of(glyph.Height)}), {Literal.Of(glyph.XOffset)}, {Literal.Of(glyph.YOffset)}, {Literal.Of(glyph.XAdvance)}"));
        code.AppendLine(",");
        ArrayOf(code, body, GeneratedTypes.KerningPair, described.Kernings.Select(static kerning =>
            $"{Literal.Of(kerning.First)}, {Literal.Of(kerning.Second)}, {Literal.Of(kerning.Amount)}"));
        code.AppendLine(");");
        code.AppendLine();

        code.Append(indent).Append("/// <summary><c>").Append(authored.Key)
            .Append(authored.Extension).Append("</c>, ").Append(Literal.Of(described.LineHeight)).Append(" pixels per line, ")
            .Append(Literal.Of(described.Glyphs.Length)).Append(" glyph(s) over ")
            .Append(Literal.Of(pages.Length)).AppendLine(" page(s).</summary>");
        code.Append(indent).Append("public static ").Append(GeneratedTypes.BitmapFont).Append(' ').Append(identifier)
            .Append(" => ").Append(field).AppendLine(";");
    }

    // An array of type, one constructor call per element's arguments, ending at its closing brace.
    private static void ArrayOf(StringBuilder code, string indent, string type, IEnumerable<string> arguments)
    {
        code.Append(indent).Append("new ").Append(type).AppendLine("[]");
        code.Append(indent).AppendLine("{");
        foreach (string element in arguments)
        {
            code.Append(indent).Append("    new ").Append(type).Append('(').Append(element).AppendLine("),");
        }

        code.Append(indent).Append('}');
    }
}

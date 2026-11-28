using System.Text;
using Capsule.Build.Registry;

namespace Capsule.Build.Fonts;

/// <summary>Writes a font's member: a <c>BitmapFont</c> carrying the metrics, glyphs and kerning its <c>.fnt</c> declares.</summary>
/// <remarks>Glyphs and kerning pairs are constant data a loop builds into the font once, and the code does not grow per glyph.</remarks>
internal static class FontMembers
{
    // A font is a class. The member hands back a single instance built once, instead of rebuilding its
    // tables per call.
    internal static void Write(StringBuilder code, string indent, string identifier, Source authored, (BmFontDescription Described, Source[] Pages) font)
    {
        (BmFontDescription described, Source[] pages) = font;
        string field = "_font" + identifier;
        string body = indent + "    ";

        code.Append(indent).Append("private static readonly ").Append(GeneratedTypes.BitmapFont).Append(' ').Append(field)
            .Append(" = ").Append(GeneratedTypes.BitmapFont).AppendLine(".FromRows(");
        code.Append(body).Append(Literal.Of(described.LineHeight)).AppendLine(",");
        code.Append(body).Append(Literal.Of(described.Base)).AppendLine(",");
        code.Append(body).Append("new ").Append(GeneratedTypes.TextureHandle).Append("[] { ")
            .AppendJoin(", ", pages.Select(static page => $"new {GeneratedTypes.TextureHandle}({Literal.Of(page.Key)}, {Literal.Of(page.Extension)})"))
            .AppendLine(" },");
        Rows(code, body, described.Glyphs.Select(static glyph => new[]
        {
            glyph.Codepoint, glyph.Page, glyph.X, glyph.Y, glyph.Width, glyph.Height, glyph.XOffset, glyph.YOffset, glyph.XAdvance,
        }));
        code.AppendLine(",");
        Rows(code, body, described.Kernings.Select(static kerning => new[] { kerning.First, kerning.Second, kerning.Amount }));
        code.AppendLine(");");
        code.AppendLine();

        code.Append(indent).Append("/// <summary><c>").Append(authored.Key)
            .Append(authored.Extension).Append("</c>, ").Append(Literal.Of(described.LineHeight)).Append(" pixels per line, ")
            .Append(Literal.Of(described.Glyphs.Length)).Append(" glyph(s) over ")
            .Append(Literal.Of(pages.Length)).AppendLine(" page(s).</summary>");
        code.Append(indent).Append("public static ").Append(GeneratedTypes.BitmapFont).Append(' ').Append(identifier)
            .Append(" => ").Append(field).AppendLine(";");
    }

    // A collection of constant ints, one row of the font's data per line, ending at its closing bracket.
    private static void Rows(StringBuilder code, string indent, IEnumerable<int[]> rows)
    {
        code.Append(indent).Append('[');
        string separator = Environment.NewLine;
        foreach (int[] row in rows)
        {
            code.Append(separator).Append(indent).Append("    ").AppendJoin(", ", row.Select(Literal.Of));
            separator = "," + Environment.NewLine;
        }

        code.Append(']');
    }
}

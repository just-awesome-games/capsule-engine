using System.Text;
using Capsule.Build.Registry;

namespace Capsule.Build.Fonts;

/// <summary>Writes a font's member: a <c>BitmapFont</c> carrying the metrics, glyphs and kerning its <c>.fnt</c> declares.</summary>
/// <remarks>
/// Glyphs and kerning pairs are constant data that a loop builds into the font once. Building each
/// glyph in code of its own compiled to code growing with every glyph a font carries.
/// </remarks>
internal static class FontMembers
{
    /// <summary>The class beside <c>CapsuleAssets</c> that builds every font from its data.</summary>
    internal const string BuilderClass = "CapsuleFontTables";

    /// <summary>The declaration of <see cref="BuilderClass"/>, which every font member calls.</summary>
    internal const string BuilderDeclaration = $$"""
            /// <summary>Builds each font from the constant data the build wrote. Generated code.</summary>
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            internal static class {{BuilderClass}}
            {
                /// <summary>
                /// A font over pages, from each glyph's codepoint, page, region as x, y, width and height, offsets
                /// and advance, and each kerning pair's first and second codepoints and amount.
                /// </summary>
                internal static {{GeneratedTypes.BitmapFont}} Font(int lineHeight, int baseline, {{GeneratedTypes.TextureHandle}}[] pages, global::System.ReadOnlySpan<int> glyphs, global::System.ReadOnlySpan<int> kernings)
                {
                    {{GeneratedTypes.Glyph}}[] cut = new {{GeneratedTypes.Glyph}}[glyphs.Length / 9];
                    for (int i = 0; i < cut.Length; i++)
                    {
                        global::System.ReadOnlySpan<int> glyph = glyphs.Slice(i * 9, 9);
                        cut[i] = new {{GeneratedTypes.Glyph}}(
                            glyph[0],
                            glyph[1],
                            new {{GeneratedTypes.TextureRegion}}(glyph[2], glyph[3], glyph[4], glyph[5]),
                            glyph[6],
                            glyph[7],
                            glyph[8]);
                    }

                    {{GeneratedTypes.KerningPair}}[] pairs = new {{GeneratedTypes.KerningPair}}[kernings.Length / 3];
                    for (int i = 0; i < pairs.Length; i++)
                    {
                        pairs[i] = new {{GeneratedTypes.KerningPair}}(kernings[i * 3], kernings[(i * 3) + 1], kernings[(i * 3) + 2]);
                    }

                    return new {{GeneratedTypes.BitmapFont}}(lineHeight, baseline, pages, cut, pairs);
                }
            }

        """;

    // A font is a class. The member hands back a single instance built once, instead of rebuilding its
    // tables per call.
    internal static void Write(StringBuilder code, string indent, string identifier, Source authored, (BmFontDescription Described, Source[] Pages) font)
    {
        (BmFontDescription described, Source[] pages) = font;
        string field = "_font" + identifier;
        string body = indent + "    ";

        code.Append(indent).Append("private static readonly ").Append(GeneratedTypes.BitmapFont).Append(' ').Append(field)
            .Append(" = global::Capsule.Generated.").Append(BuilderClass).AppendLine(".Font(");
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

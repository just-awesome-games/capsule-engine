using System.Globalization;

namespace Capsule.Build.Fonts;

// One glyph as the font declares it. Page holds the font's page id until the parser resolves it to
// an index into PageFiles.
internal readonly record struct BmGlyph(
    int Codepoint,
    int Page,
    int X,
    int Y,
    int Width,
    int Height,
    int XOffset,
    int YOffset,
    int XAdvance);

internal readonly record struct BmKerning(int First, int Second, int Amount);

// What the build reads out of one '.fnt'. Pages are in page-id order, glyphs ascend by codepoint,
// and kernings ascend by pair.
internal sealed record BmFontDescription(int LineHeight, int Base, string[] PageFiles, BmGlyph[] Glyphs, BmKerning[] Kernings);

// Reads the text flavour of the BMFont format. The font is known at build time, so the generated
// registry carries it as literal data and nothing is parsed at run time.
internal static class BmFontParser
{
    /// <summary>The extension a page ships as.</summary>
    private const string PageExtension = ".png";

    /// <summary>Reads <paramref name="text"/>.</summary>
    /// <exception cref="FormatException">
    /// The source is the XML or binary flavour, channel-packed, malformed, or cuts a glyph its pages
    /// cannot hold. The message names the line the defect is on.
    /// </exception>
    internal static BmFontDescription Parse(string text)
    {
        // The format has three flavours. Only text is read, and the other two are named so they do
        // not fail later as malformed text.
        if (text.StartsWith("BMF", StringComparison.Ordinal))
        {
            throw new FormatException("is a binary BMFont file. Capsule reads the text flavour. Export the font as text.");
        }

        if (text.TrimStart().StartsWith('<'))
        {
            throw new FormatException("is an XML BMFont file. Capsule reads the text flavour. Export the font as text.");
        }

        int lineHeight = 0;
        int baseline = 0;
        int scaleWidth = 0;
        int scaleHeight = 0;
        int commonLine = 0;
        SortedDictionary<int, (string File, int Line)> pages = [];
        List<(BmGlyph Glyph, int Line)> glyphs = [];
        HashSet<int> codepoints = [];
        List<(BmKerning Kerning, int Line)> kernings = [];

        string[] lines = text.Split('\n');

        for (int line = 0; line < lines.Length; line++)
        {
            Fields fields = Fields.Split(lines[line], line);

            switch (fields.Tag)
            {
                case "common":
                    commonLine = line;
                    lineHeight = fields.Require("lineHeight");
                    baseline = fields.Require("base");
                    scaleWidth = fields.Require("scaleW");
                    scaleHeight = fields.Require("scaleH");
                    fields.Require("pages");

                    if (fields.Optional("packed") != 0)
                    {
                        throw At(line, "declares packed=1. Capsule draws a page as an RGBA texture and reads no channel-packed glyph. Bake the font unpacked.");
                    }

                    break;

                case "page":
                    pages[fields.Require("id")] = (fields.RequireText("file"), line);
                    break;

                case "char":
                    BmGlyph glyph = new(
                        fields.Require("id"),
                        fields.Require("page"),
                        fields.Require("x"),
                        fields.Require("y"),
                        fields.Require("width"),
                        fields.Require("height"),
                        fields.Require("xoffset"),
                        fields.Require("yoffset"),
                        fields.Require("xadvance"));

                    if (!codepoints.Add(glyph.Codepoint))
                    {
                        throw At(line, Invariant($"declares codepoint {glyph.Codepoint} twice."));
                    }

                    glyphs.Add((glyph, line));
                    break;

                case "kerning":
                    kernings.Add((new BmKerning(fields.Require("first"), fields.Require("second"), fields.Require("amount")), line));
                    break;

                // Declared counts are ignored. The page, char and kerning lines define the font.
                default:
                    break;
            }
        }

        if (lineHeight <= 0)
        {
            throw At(commonLine, Invariant($"declares lineHeight={lineHeight}. A line must be at least one pixel tall."));
        }

        if (pages.Count == 0)
        {
            throw new FormatException("declares no page. A font must be baked onto at least one page.");
        }

        string[] pageFiles = new string[pages.Count];
        Dictionary<int, int> pageIndex = new(pages.Count);

        foreach ((int id, (string file, int line)) in pages)
        {
            if (!string.Equals(Path.GetExtension(file), PageExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw At(line, $"names page \"{file}\". A page must ship as {PageExtension}.");
            }

            pageFiles[pageIndex.Count] = file.Replace('\\', '/');
            pageIndex.Add(id, pageIndex.Count);
        }

        BmGlyph[] placed = new BmGlyph[glyphs.Count];
        for (int i = 0; i < glyphs.Count; i++)
        {
            (BmGlyph glyph, int line) = glyphs[i];

            if (!pageIndex.TryGetValue(glyph.Page, out int index))
            {
                throw At(line, Invariant($"puts codepoint {glyph.Codepoint} on page {glyph.Page} and declares no file for that page."));
            }

            // Widened to long: two ints that each parse can sum past int range, and a wrapped sum
            // would read as a rectangle inside the page.
            if (glyph.X < 0
                || glyph.Y < 0
                || glyph.Width < 0
                || glyph.Height < 0
                || (long)glyph.X + glyph.Width > scaleWidth
                || (long)glyph.Y + glyph.Height > scaleHeight)
            {
                throw At(line, Invariant(
                    $"cuts codepoint {glyph.Codepoint} at ({glyph.X}, {glyph.Y}) {glyph.Width}x{glyph.Height}, outside its {scaleWidth}x{scaleHeight} page."));
            }

            placed[i] = glyph with { Page = index };
        }

        foreach ((BmKerning kerning, int line) in kernings)
        {
            if (!codepoints.Contains(kerning.First) || !codepoints.Contains(kerning.Second))
            {
                throw At(line, Invariant($"kerns {kerning.First} against {kerning.Second} and carries no glyph for one of them."));
            }
        }

        return new BmFontDescription(
            lineHeight,
            baseline,
            pageFiles,
            [.. placed.OrderBy(static glyph => glyph.Codepoint)],
            [.. kernings.Select(static entry => entry.Kerning).OrderBy(static kerning => kerning.First).ThenBy(static kerning => kerning.Second)]);
    }

    private static FormatException At(int line, string message) =>
        new(Invariant($"line {line + 1} ") + message);

    private static string Invariant(FormattableString message) => FormattableString.Invariant(message);

    // One line of the text flavour: a tag, then key=value pairs with optionally quoted values. A
    // field the font is measured by must be present and must parse, since a zero would bake an
    // invisible glyph or collapse line spacing. Other keys are ignored.
    private readonly struct Fields(string tag, int line, Dictionary<string, string> values)
    {
        internal string Tag { get; } = tag;

        internal static Fields Split(string text, int line)
        {
            int i = 0;
            SkipSpace(text, ref i);

            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            string tag = text[start..i];
            Dictionary<string, string> values = new(StringComparer.Ordinal);

            while (tag.Length > 0)
            {
                SkipSpace(text, ref i);
                if (i >= text.Length)
                {
                    break;
                }

                int keyStart = i;
                while (i < text.Length && text[i] != '=' && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                string key = text[keyStart..i];
                if (i >= text.Length || text[i] != '=')
                {
                    continue;
                }

                i++;
                bool quoted = i < text.Length && text[i] == '"';
                if (quoted)
                {
                    i++;
                }

                int valueStart = i;
                while (i < text.Length && (quoted ? text[i] != '"' : !char.IsWhiteSpace(text[i])))
                {
                    i++;
                }

                values[key] = text[valueStart..i];
                if (quoted && i < text.Length)
                {
                    i++;
                }
            }

            return new Fields(tag, line, values);
        }

        /// <summary>A whole-number field the font is measured by.</summary>
        internal int Require(string key)
        {
            string value = RequireText(key);

            // TryParse also refuses an overflowing literal. A value too large for an int fails
            // here instead of wrapping into a plausible one.
            return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : throw At(line, $"declares {Tag} {key}=\"{value}\". That is not a whole number the font can be measured by.");
        }

        /// <summary>A textual field the font is measured by.</summary>
        internal string RequireText(string key) =>
            values.TryGetValue(key, out string? text) && text.Length > 0
                ? text
                : throw At(line, $"declares a {Tag} line with no {key}.");

        /// <summary>A field that may be absent. Reads as zero when missing.</summary>
        internal int Optional(string key) =>
            values.TryGetValue(key, out string? value)
            && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;

        private static void SkipSpace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }
    }
}

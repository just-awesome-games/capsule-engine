using System.Globalization;
using System.Text;

namespace Capsule.Generators;

// One glyph as the font declares it, before it becomes a Capsule.Rendering.Glyph literal. Page is
// the id the font gave it until the description resolves it into an index into PageFiles.
internal readonly struct BmGlyph(
    int codepoint,
    int page,
    int x,
    int y,
    int width,
    int height,
    int xOffset,
    int yOffset,
    int xAdvance)
{
    internal readonly int Codepoint = codepoint;
    internal readonly int Page = page;
    internal readonly int X = x;
    internal readonly int Y = y;
    internal readonly int Width = width;
    internal readonly int Height = height;
    internal readonly int XOffset = xOffset;
    internal readonly int YOffset = yOffset;
    internal readonly int XAdvance = xAdvance;

    internal BmGlyph OnPage(int index) =>
        new(Codepoint, index, X, Y, Width, Height, XOffset, YOffset, XAdvance);
}

internal readonly struct BmKerning(int first, int second, int amount)
{
    internal readonly int First = first;
    internal readonly int Second = second;
    internal readonly int Amount = amount;
}

// What the generator reads out of one '.fnt': line metrics, the pages it was baked onto in page-id
// order, the glyph cut for each codepoint ascending, and the kerning between them ascending by pair.
internal sealed class BmFontDescription(
    int lineHeight,
    int baseline,
    string[] pageFiles,
    BmGlyph[] glyphs,
    BmKerning[] kernings)
{
    internal readonly int LineHeight = lineHeight;
    internal readonly int Base = baseline;
    internal readonly string[] PageFiles = pageFiles;
    internal readonly BmGlyph[] Glyphs = glyphs;
    internal readonly BmKerning[] Kernings = kernings;
}

// Reads the text flavour of the BMFont format. The whole font is known at compile time, so the
// generated registry carries it as literal data and nothing is parsed at run time.
internal static class BmFontParser
{
    /// <summary>The extension the fonts domain admits for a font description.</summary>
    internal const string BmFontExtension = ".fnt";

    /// <summary>The extension a page ships as.</summary>
    internal const string BmFontPageExtension = ".png";

    /// <summary>
    /// Reads <paramref name="text"/>. Null with <paramref name="error"/> set when the source is of
    /// the XML or binary flavour, channel-packed, malformed, or describes a glyph its own pages
    /// cannot hold; the message states the defect and, where one exists, the fix.
    /// </summary>
    /// <param name="errorLine">
    /// The zero-based line the defect is on, or zero where it belongs to the file rather than to
    /// one line.
    /// </param>
    internal static BmFontDescription? Parse(string text, out string? error, out int errorLine)
    {
        errorLine = 0;

        // The three flavours the format has. Only the text one is shipped, and the other two are
        // named rather than left to fail as malformed text.
        if (text.StartsWith("BMF", StringComparison.Ordinal))
        {
            error = "is a binary BMFont file; Capsule reads the text flavour, so export the font as text.";

            return null;
        }

        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (character == '<')
            {
                error = "is an XML BMFont file; Capsule reads the text flavour, so export the font as text.";

                return null;
            }

            break;
        }

        return Read(text, out error, ref errorLine);
    }

    private static BmFontDescription? Read(string text, out string? error, ref int errorLine)
    {
        int lineHeight = 0;
        int baseline = 0;
        int scaleWidth = 0;
        int scaleHeight = 0;
        int commonLine = 0;
        SortedDictionary<int, string> pages = new();
        Dictionary<int, int> pageLines = new();
        List<BmGlyph> glyphs = [];
        List<int> glyphLines = [];
        HashSet<int> codepoints = [];
        List<BmKerning> kernings = [];
        List<int> kerningLines = [];

        string[] lines = text.Split('\n');

        for (int line = 0; line < lines.Length; line++)
        {
            Fields fields = Fields.Split(lines[line]);

            // Every defect this loop refuses is on the line being read; the checks after it name
            // the line the entity they refuse was declared on.
            errorLine = line;

            switch (fields.Tag)
            {
                case "common":
                    commonLine = line;

                    if (fields.Require("lineHeight", out lineHeight, out error)
                        && fields.Require("base", out baseline, out error)
                        && fields.Require("scaleW", out scaleWidth, out error)
                        && fields.Require("scaleH", out scaleHeight, out error)
                        && fields.Require("pages", out _, out error))
                    {
                        if (fields.Optional("packed") != 0)
                        {
                            error = "declares packed=1; Capsule draws a page as an ordinary RGBA texture and reads no channel-packed glyph, so bake the font unpacked.";

                            return null;
                        }

                        break;
                    }

                    return null;

                case "page":
                    if (!fields.Require("id", out int pageId, out error)
                        || !fields.RequireText("file", out string pageFile, out error))
                    {
                        return null;
                    }

                    pages[pageId] = pageFile;
                    pageLines[pageId] = line;

                    break;

                case "char":
                    if (!fields.Require("id", out int codepoint, out error)
                        || !fields.Require("page", out int page, out error)
                        || !fields.Require("x", out int x, out error)
                        || !fields.Require("y", out int y, out error)
                        || !fields.Require("width", out int width, out error)
                        || !fields.Require("height", out int height, out error)
                        || !fields.Require("xoffset", out int xOffset, out error)
                        || !fields.Require("yoffset", out int yOffset, out error)
                        || !fields.Require("xadvance", out int xAdvance, out error))
                    {
                        return null;
                    }

                    if (!codepoints.Add(codepoint))
                    {
                        error = Invariant($"declares codepoint {codepoint} twice.");

                        return null;
                    }

                    glyphs.Add(new BmGlyph(codepoint, page, x, y, width, height, xOffset, yOffset, xAdvance));
                    glyphLines.Add(line);

                    break;

                case "kerning":
                    if (!fields.Require("first", out int first, out error)
                        || !fields.Require("second", out int second, out error)
                        || !fields.Require("amount", out int amount, out error))
                    {
                        return null;
                    }

                    kernings.Add(new BmKerning(first, second, amount));
                    kerningLines.Add(line);

                    break;

                // "info", "chars", "kernings" and anything else: informational. A declared count
                // that disagrees with the lines is ignored — the lines are the font.
                default:
                    break;
            }
        }

        errorLine = commonLine;

        if (lineHeight <= 0)
        {
            error = Invariant($"declares lineHeight={lineHeight}; a line has to be at least one pixel tall.");

            return null;
        }

        if (pages.Count == 0)
        {
            errorLine = 0;
            error = "declares no page; a font is baked onto at least one.";

            return null;
        }

        string[] pageFiles = new string[pages.Count];
        Dictionary<int, int> pageIndex = new(pages.Count);

        foreach (KeyValuePair<int, string> page in pages)
        {
            string file = page.Value.Replace('\\', '/');

            if (!string.Equals(Path.GetExtension(file), BmFontPageExtension, StringComparison.OrdinalIgnoreCase))
            {
                errorLine = pageLines[page.Key];
                error = $"names page \"{page.Value}\", and a page ships as {BmFontPageExtension}.";

                return null;
            }

            int index = pageIndex.Count;
            pageIndex.Add(page.Key, index);
            pageFiles[index] = file;
        }

        for (int i = 0; i < glyphs.Count; i++)
        {
            BmGlyph glyph = glyphs[i];
            errorLine = glyphLines[i];

            if (!pageIndex.TryGetValue(glyph.Page, out int index))
            {
                error = Invariant($"puts codepoint {glyph.Codepoint} on page {glyph.Page}, which it declares no file for.");

                return null;
            }

            // Widened: two ints that each parse can still sum past what one holds, and a wrapped
            // sum would read as a rectangle inside the page.
            if (glyph.X < 0
                || glyph.Y < 0
                || glyph.Width < 0
                || glyph.Height < 0
                || (long)glyph.X + glyph.Width > scaleWidth
                || (long)glyph.Y + glyph.Height > scaleHeight)
            {
                error = Invariant(
                    $"cuts codepoint {glyph.Codepoint} from ({glyph.X}, {glyph.Y}) {glyph.Width}x{glyph.Height}, which is outside its {scaleWidth}x{scaleHeight} page.");

                return null;
            }

            glyphs[i] = glyph.OnPage(index);
        }

        for (int i = 0; i < kernings.Count; i++)
        {
            BmKerning kerning = kernings[i];

            if (!codepoints.Contains(kerning.First) || !codepoints.Contains(kerning.Second))
            {
                errorLine = kerningLines[i];
                error = Invariant(
                    $"kerns {kerning.First} against {kerning.Second}, and it carries no glyph for one of them.");

                return null;
            }
        }

        glyphs.Sort(static (left, right) => left.Codepoint.CompareTo(right.Codepoint));
        kernings.Sort(static (left, right) =>
        {
            int byFirst = left.First.CompareTo(right.First);

            return byFirst != 0 ? byFirst : left.Second.CompareTo(right.Second);
        });

        error = null;
        errorLine = 0;

        return new BmFontDescription(lineHeight, baseline, pageFiles, [.. glyphs], [.. kernings]);
    }

    private static string Invariant(FormattableString message) => FormattableString.Invariant(message);

    // One line of the text flavour: a tag, then key=value pairs, the value optionally quoted. A
    // field the font is measured by is required to be there and to parse — a missing or malformed
    // one silently read as zero would bake an invisible glyph or collapse a line's spacing — while
    // every key Capsule does not read is ignored whatever it holds.
    private readonly struct Fields
    {
        private readonly Dictionary<string, string>? _values;

        private Fields(string tag, Dictionary<string, string>? values)
        {
            Tag = tag;
            _values = values;
        }

        internal string Tag { get; }

        internal static Fields Split(string line)
        {
            int i = 0;
            SkipSpace(line, ref i);

            int start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i]))
            {
                i++;
            }

            if (i == start)
            {
                return new Fields(string.Empty, null);
            }

            string tag = line.Substring(start, i - start);
            Dictionary<string, string> values = new(StringComparer.Ordinal);

            while (true)
            {
                SkipSpace(line, ref i);
                if (i >= line.Length)
                {
                    break;
                }

                int keyStart = i;
                while (i < line.Length && line[i] != '=' && !char.IsWhiteSpace(line[i]))
                {
                    i++;
                }

                string key = line.Substring(keyStart, i - keyStart);
                if (i >= line.Length || line[i] != '=')
                {
                    continue;
                }

                i++;
                string value;

                if (i < line.Length && line[i] == '"')
                {
                    i++;
                    int quoted = i;
                    while (i < line.Length && line[i] != '"')
                    {
                        i++;
                    }

                    value = line.Substring(quoted, i - quoted);
                    if (i < line.Length)
                    {
                        i++;
                    }
                }
                else
                {
                    int plain = i;
                    while (i < line.Length && !char.IsWhiteSpace(line[i]))
                    {
                        i++;
                    }

                    value = line.Substring(plain, i - plain);
                }

                values[key] = value;
            }

            return new Fields(tag, values);
        }

        /// <summary>A whole-number field the font is measured by.</summary>
        internal bool Require(string key, out int parsed, out string? error)
        {
            parsed = 0;

            if (!RequireText(key, out string value, out error))
            {
                return false;
            }

            // TryParse refuses an overflowing literal too, so a value no int can hold fails here
            // rather than wrapping into a plausible one.
            if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed))
            {
                return true;
            }

            error = $"declares {Tag} {key}=\"{value}\", which is no whole number Capsule can measure the font by.";

            return false;
        }

        /// <summary>A textual field the font is measured by.</summary>
        internal bool RequireText(string key, out string value, out string? error)
        {
            value = _values is not null && _values.TryGetValue(key, out string? text) ? text : string.Empty;
            if (value.Length > 0)
            {
                error = null;

                return true;
            }

            error = $"declares a {Tag} line carrying no {key}.";

            return false;
        }

        /// <summary>A field that may be absent, which reads as <paramref name="fallback"/>.</summary>
        internal int Optional(string key, int fallback = 0) =>
            _values is not null
            && _values.TryGetValue(key, out string? value)
            && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;

        private static void SkipSpace(string line, ref int index)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }
        }
    }
}

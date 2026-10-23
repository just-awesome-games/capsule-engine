using Capsule.Assets;

namespace Capsule.Rendering;

// What a Label's layout depends on besides the text itself. Box is the box width in font pixels where the
// label's X extent is positive, and -1 where the run sets its own width. Exclusion is the rect the lines flow
// around, in font pixels from the box's top-left, and null wherever the label's settings leave it no effect.
internal readonly record struct LabelLayoutKey(
    int TextVersion,
    BitmapFont Font,
    int Box,
    TextWrap Wrap,
    HorizontalAlignment Alignment,
    bool Rich,
    int FirstLine,
    int? MaxLines,
    int LineSpacing,
    Rect? Exclusion);

// Where one glyph of a laid-out label sits, in font pixels from the run's origin, and the span colour it takes.
internal readonly record struct LabelGlyph(int Page, TextureRegion Region, int X, int Y, int Index, ColorRgba Tint);

// A label's laid-out run: the markup stripped, every glyph placed, and the extent measured. The buffers
// only grow, and a rebuild allocates nothing once they have held a run this long. A label asks it to Update
// each draw, and it lays the run out again only when the key changed.
internal sealed class LabelLayout
{
    private readonly record struct ColorRun(int Start, ColorRgba Color);

    private char[] _chars = [];
    private ColorRun[] _runs = [];
    private ColorRgba[] _stack = [];
    private LabelGlyph[] _glyphs = [];
    private int[] _lineStarts = [];
    private int _length;
    private int _codepoints;
    private int _runCount;
    private int _depth;
    private int _glyphCount;

    // The whole text's line starts are walked on the first read after a rebuild. A label that never asks
    // pays nothing for them.
    private bool _linesWalked;
    private int _lineCount;
    private int _codepointCount;

    // Whether the last char put was a high surrogate still waiting for its low half, which then adds no codepoint.
    private bool _openHigh;

    private bool _built;
    private LabelLayoutKey _key;

    // True when the run has no codepoint to draw.
    internal bool IsEmpty { get; private set; }

    // The measured run in font pixels: the widest line, and the lines' total height.
    internal int Width { get; private set; }

    internal int Height { get; private set; }

    internal ReadOnlySpan<LabelGlyph> Glyphs => _glyphs.AsSpan(0, _glyphCount);

    // The lines the whole text lays out to, up to the last that draws, whatever the window.
    // text is the source the layout was last updated with.
    internal int LineCount(ReadOnlySpan<char> text)
    {
        WalkLines(text);

        return _lineCount;
    }

    // The codepoint index, markup excluded, of the line's first codepoint, and the text's codepoint count at
    // or past LineCount.
    internal int LineStart(ReadOnlySpan<char> text, int line)
    {
        WalkLines(text);

        return line < _lineCount ? _lineStarts[line] : _codepointCount;
    }

    internal void Update(ReadOnlySpan<char> text, in LabelLayoutKey key)
    {
        if (!_built || !key.Equals(_key))
        {
            Rebuild(text, in key);
        }
    }

    private void Rebuild(ReadOnlySpan<char> text, in LabelLayoutKey key)
    {
        _built = true;
        _key = key;
        _glyphCount = 0;
        _runCount = 0;
        _linesWalked = false;

        ReadOnlySpan<char> shown = text;
        if (key.Rich)
        {
            Parse(text);
            shown = _chars.AsSpan(0, _length);
        }

        BitmapFont font = key.Font;

        // A left-aligned run reads its own extent off the placements. Any other alignment needs the extent
        // first, because an auto-sized box is as wide as the widest line.
        bool left = key.Alignment == HorizontalAlignment.Left;
        int given = Math.Max(key.Box, 0);
        int width = 0;
        int lines = 0;
        int box = given;

        if (!left)
        {
            (width, lines) = GlyphRun.Extent(font, shown, key.Wrap == TextWrap.Word ? given : 0, TextWrap.Word, key.FirstLine, key.MaxLines, Exclusion(in key));
            box = key.Box >= 0 ? key.Box : width;
        }

        IsEmpty = shown.IsEmpty;

        int pitch = font.LineHeight + key.LineSpacing;
        int cursor = 0;
        // An unsized box only aligns. Wrapping at the window's width would break lines outside the window.
        TextWrap wrap = key.Box >= 0 ? key.Wrap : TextWrap.None;

        foreach (GlyphPlacement placed in new GlyphRun(font, shown, box, wrap, key.Alignment, key.FirstLine, key.MaxLines, Exclusion(in key)))
        {
            int line = placed.Line - key.FirstLine;
            if (left)
            {
                width = Math.Max(width, placed.PenX + placed.Glyph.XAdvance);
                lines = Math.Max(lines, line + 1);
            }

            Glyph glyph = placed.Glyph;
            Put(new LabelGlyph(
                glyph.Page,
                glyph.Region,
                placed.PenX + glyph.XOffset,
                (line * pitch) + glyph.YOffset,
                placed.Index,
                TintAt(placed.Index, ref cursor)));
        }

        Width = width;
        Height = lines > 0 ? (lines * font.LineHeight) + ((lines - 1) * key.LineSpacing) : 0;
    }

    // Walks the whole text's lines, with the box and wrap the glyphs were laid out under, and no window.
    private void WalkLines(ReadOnlySpan<char> text)
    {
        if (_linesWalked)
        {
            return;
        }

        _linesWalked = true;
        _lineCount = 0;

        ReadOnlySpan<char> shown = _key.Rich ? _chars.AsSpan(0, _length) : text;
        int box = Math.Max(_key.Box, 0);
        GlyphRun run = new(_key.Font, shown, box, _key.Wrap, HorizontalAlignment.Left, 0, null, Exclusion(in _key));

        int count = 0;
        while (run.MoveNextLine(out int start, out bool draws))
        {
            if (count == _lineStarts.Length)
            {
                Array.Resize(ref _lineStarts, Math.Max(8, count * 2));
            }

            _lineStarts[count++] = start;
            if (draws)
            {
                _lineCount = count;
            }
        }

        run.MoveNextLine(out _codepointCount, out _);
    }

    // The key's exclusion as the layout walks it. Its rows repeat every MaxLines lines, one page each.
    private static TextExclusion? Exclusion(in LabelLayoutKey key) => key.Exclusion is { } area
        ? new TextExclusion(area, key.Font.LineHeight + key.LineSpacing, key.Font.LineHeight, key.MaxLines ?? 0)
        : null;

    // Strips the markup into _chars and records the colour runs it asked for.
    private void Parse(ReadOnlySpan<char> source)
    {
        _length = 0;
        _codepoints = 0;
        _depth = 0;
        _openHigh = false;
        SetColor(ColorRgba.White);

        int at = 0;
        while (at < source.Length)
        {
            ReadOnlySpan<char> rest = source[at..];
            if (LabelMarkup.TryReadTag(rest, out MarkupTag tag, out int length) && Apply(tag))
            {
                at += length;

                continue;
            }

            // A tag Apply refuses draws as written, read on from the char after its '[' so a tag inside it
            // still applies.
            Put(rest[0]);
            at += LabelMarkup.IsEscape(rest) ? 2 : 1;
        }
    }

    // Every tag RichText reads. A handler that returns false leaves its tag drawn as written.
    private bool Apply(MarkupTag tag) => tag.Name switch
    {
        "color" => tag.Closing ? PopColor() : PushColor(tag.Value),
        _ => false,
    };

    private bool PushColor(ReadOnlySpan<char> hex)
    {
        if (!ColorRgba.TryReadHex(hex, out ColorRgba color))
        {
            return false;
        }

        if (_depth == _stack.Length)
        {
            Array.Resize(ref _stack, Math.Max(4, _depth * 2));
        }

        _stack[_depth++] = color;
        SetColor(color);

        return true;
    }

    private bool PopColor()
    {
        if (_depth == 0)
        {
            return false;
        }

        _depth--;
        SetColor(_depth > 0 ? _stack[_depth - 1] : ColorRgba.White);

        return true;
    }

    private void SetColor(ColorRgba color)
    {
        // A run that would start where the last one did replaces it, and one that changes nothing is dropped.
        if (_runCount > 0 && _runs[_runCount - 1].Start == _codepoints)
        {
            _runCount--;
        }

        if (_runCount > 0 && _runs[_runCount - 1].Color == color)
        {
            return;
        }

        if (_runCount == _runs.Length)
        {
            Array.Resize(ref _runs, Math.Max(4, _runCount * 2));
        }

        _runs[_runCount++] = new ColorRun(_codepoints, color);
    }

    private ColorRgba TintAt(int index, ref int cursor)
    {
        if (_runCount == 0)
        {
            return ColorRgba.White;
        }

        while (cursor + 1 < _runCount && _runs[cursor + 1].Start <= index)
        {
            cursor++;
        }

        return _runs[cursor].Color;
    }

    private void Put(in LabelGlyph glyph)
    {
        if (_glyphCount == _glyphs.Length)
        {
            Array.Resize(ref _glyphs, Math.Max(16, _glyphs.Length * 2));
        }

        _glyphs[_glyphCount++] = glyph;
    }

    private void Put(char c)
    {
        if (_length == _chars.Length)
        {
            Array.Resize(ref _chars, Math.Max(32, _length * 2));
        }

        _chars[_length++] = c;

        // A low surrogate completes the pair its high half opened, and the pair is one codepoint.
        if (char.IsLowSurrogate(c) && _openHigh)
        {
            _openHigh = false;

            return;
        }

        _codepoints++;
        _openHigh = char.IsHighSurrogate(c);
    }
}

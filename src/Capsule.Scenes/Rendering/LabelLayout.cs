using Capsule.Assets;

namespace Capsule.Rendering;

// What a Label's layout depends on besides the text itself. Box is the box width in font pixels where the
// label's X extent is positive, and -1 where the run sets its own width.
internal readonly record struct LabelLayoutKey(
    int TextVersion,
    BitmapFont Font,
    int Box,
    TextWrap Wrap,
    HorizontalAlignment Alignment,
    bool Rich);

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
    private int _length;
    private int _codepoints;
    private int _runCount;
    private int _depth;
    private int _glyphCount;

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
            (width, lines) = GlyphRun.Extent(font, shown, key.Wrap == TextWrap.Word ? given : 0, TextWrap.Word);
            box = key.Box >= 0 ? key.Box : width;
        }

        IsEmpty = shown.IsEmpty;

        int cursor = 0;
        foreach (GlyphPlacement placed in new GlyphRun(font, shown, box, key.Wrap, key.Alignment))
        {
            if (left)
            {
                width = Math.Max(width, placed.PenX + placed.Glyph.XAdvance);
                lines = Math.Max(lines, placed.Line + 1);
            }

            Glyph glyph = placed.Glyph;
            Put(new LabelGlyph(
                glyph.Page,
                glyph.Region,
                placed.PenX + glyph.XOffset,
                (placed.Line * font.LineHeight) + glyph.YOffset,
                placed.Index,
                TintAt(placed.Index, ref cursor)));
        }

        Width = width;
        Height = lines * font.LineHeight;
    }

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

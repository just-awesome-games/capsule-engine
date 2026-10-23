using System.Text;

namespace Capsule.Rendering;

/// <summary>Where one glyph of a laid-out run sits, in font pixels from the run's origin, which is the top-left corner of its first line.</summary>
/// <param name="Glyph">The glyph to draw.</param>
/// <param name="PenX">
/// Font pixels right from the origin to this glyph's pen, alignment included.
/// <see cref="Rendering.Glyph.XOffset"/> applies on top of it.
/// </param>
/// <param name="Line">
/// Lines below the first. The glyph's line starts this times <see cref="BitmapFont.LineHeight"/> below
/// the origin, and <see cref="Rendering.Glyph.YOffset"/> applies on top of it.
/// </param>
/// <param name="Index">
/// The glyph's codepoint position in the run's text. It counts every codepoint the text carries,
/// including line breaks, codepoints the font has no glyph for, and the spaces a wrap broke at. It
/// rises over the run and skips the positions that draw nothing.
/// </param>
public readonly record struct GlyphPlacement(Glyph Glyph, int PenX, int Line, int Index);

/// <summary>
/// The layout pass over a font and a run of text, in font pixels and free of any texture, frame or
/// camera. <c>foreach</c> it to place every glyph the run draws, in reading order.
/// </summary>
/// <remarks>
/// <see cref="BitmapFont.Measure(ReadOnlySpan{char})"/> and
/// <see cref="FrameView.Add(in TextIntent)"/> both enumerate it, and a measure matches what is
/// drawn. Enumerating it allocates nothing and throws nothing. A consumer that draws its own glyphs
/// lays the run out here and adds its own sprites to a <see cref="FrameView"/>.
/// </remarks>
public ref struct GlyphRun
{
    private readonly BitmapFont _font;
    private readonly ReadOnlySpan<char> _text;
    private readonly int _boxWidth;
    private readonly TextWrap _wrap;
    private readonly HorizontalAlignment _alignment;

    // The half-open range of laid-out lines that place glyphs. Lines before it are walked. Indices
    // still count the whole text, and the walk stops at its end.
    private readonly int _firstLine;
    private readonly int _endLine;

    // The rect lines flow around, or null for a run whose every line spans the box.
    private readonly TextExclusion? _exclusion;

    // The half-open char range of the line being emitted, and where the next line begins. The gap
    // between _lineEnd and _nextLine holds what the break consumed, a newline or the space a wrap broke
    // at.
    private int _lineEnd;
    private int _nextLine;
    private bool _lineOpen;

    // Whether the open line draws a glyph.
    private bool _lineDraws;

    // Font pixels this line is shifted right by to satisfy _alignment.
    private int _shift;

    private int _index;
    private int _codepoint;
    private int _pen;
    private int _line;

    // The last codepoint drawn on this line, or -1 at the start of a line. A codepoint the font has no
    // glyph for leaves it alone. The pair either side of it still kerns.
    private int _previous;

    /// <summary>The glyph this enumerator has reached.</summary>
    public GlyphPlacement Current { get; private set; }

    // One left-aligned block, with each line as long as the text makes it.
    internal GlyphRun(BitmapFont font, ReadOnlySpan<char> text)
        : this(font, text, 0, TextWrap.None, HorizontalAlignment.Left)
    {
    }

    /// <summary>Lays <paramref name="text"/> out inside a box of <paramref name="boxWidth"/>.</summary>
    /// <param name="font">The font the run is laid out in.</param>
    /// <param name="text">
    /// The text to lay out. <c>\n</c> starts a new line, <c>\r</c> is ignored, and a codepoint the
    /// font carries no glyph for draws nothing and advances nothing.
    /// </param>
    /// <param name="boxWidth">
    /// The box's width in font pixels, which lines wrap inside and are aligned within. Zero or less
    /// means no box, and lines then neither wrap nor shift whatever the other two arguments say.
    /// </param>
    /// <param name="wrap">Whether a line too wide for the box breaks inside it.</param>
    /// <param name="alignment">Where each line sits between the box's edges.</param>
    public GlyphRun(BitmapFont font, ReadOnlySpan<char> text, int boxWidth, TextWrap wrap, HorizontalAlignment alignment)
        : this(font, text, boxWidth, wrap, alignment, 0, null)
    {
    }

    // The same layout placing only the lines from firstLine, at most maxLines of them, and every line where
    // maxLines is null. Line and Index stay counted over the whole text. exclusion narrows the lines it
    // crosses only in a word-wrapped run with a box, and only where it leaves room in some row of a page.
    internal GlyphRun(
        BitmapFont font,
        ReadOnlySpan<char> text,
        int boxWidth,
        TextWrap wrap,
        HorizontalAlignment alignment,
        int firstLine,
        int? maxLines,
        TextExclusion? exclusion = null)
    {
        ArgumentNullException.ThrowIfNull(font);

        _font = font;
        _text = text;
        _boxWidth = boxWidth;
        _wrap = wrap;
        _alignment = alignment;
        _firstLine = firstLine;
        _endLine = maxLines is { } max ? (int)Math.Min((long)firstLine + max, int.MaxValue) : int.MaxValue;
        _previous = -1;

        if (exclusion is { } area && wrap == TextWrap.Word && boxWidth > 0 && !area.FillsEveryRow(boxWidth))
        {
            _exclusion = area;
        }
    }

    /// <summary>Returns a copy of the run. A <c>foreach</c> leaves this one where it was.</summary>
    public readonly GlyphRun GetEnumerator() => this;

    /// <summary>Advances to the next glyph the run draws.</summary>
    /// <returns>Whether there was one.</returns>
    public bool MoveNext()
    {
        while (_index < _text.Length)
        {
            if (!_lineOpen)
            {
                if (_line >= _endLine)
                {
                    return false;
                }

                OpenLine();

                // A line before the window places nothing, and its codepoints are only counted.
                if (_line < _firstLine)
                {
                    SkipTo(_lineEnd);
                }
            }

            if (_index >= _lineEnd)
            {
                // A break's codepoints are counted but never drawn. Index stays the position in the
                // text whatever the layout did with the character.
                SkipTo(_nextLine);

                if (_index >= _text.Length)
                {
                    return false;
                }

                _line++;
                _pen = 0;
                _previous = -1;
                _lineOpen = false;

                continue;
            }

            bool drawn = TryAdvance(_index, _previous, ref _pen, out int codepoint, out Glyph glyph, out int consumed);
            _index += consumed;
            int index = _codepoint++;

            if (!drawn)
            {
                continue;
            }

            Current = new GlyphPlacement(glyph, _pen + _shift, _line, index);
            _pen += glyph.XAdvance;
            _previous = codepoint;

            return true;
        }

        return false;
    }

    // Advances to the next laid-out line without placing its glyphs. start is the codepoint index its first
    // codepoint sits at, and draws whether it places a glyph. The window does not apply.
    internal bool MoveNextLine(out int start, out bool draws)
    {
        start = _codepoint;
        draws = false;

        if (_index >= _text.Length)
        {
            return false;
        }

        OpenLine();
        draws = _lineDraws;
        SkipTo(_nextLine);
        _line++;

        return true;
    }

    // Counts the codepoints up to end without placing them.
    private void SkipTo(int end)
    {
        while (_index < end)
        {
            CodepointAt(_index, out int skipped);
            _index += skipped;
            _codepoint++;
        }
    }

    // Finds where the line starting at _index ends and how far it is shifted. This is one measuring walk
    // per line, and laying out a run costs two passes over it.
    private void OpenLine()
    {
        _lineOpen = true;
        _lineDraws = false;
        _lineEnd = _text.Length;
        _nextLine = _text.Length;
        _shift = 0;

        // The part of the box this line lays out in. Without an exclusion it is the whole box.
        int lead = 0;
        int span = _boxWidth;
        if (_exclusion is { } exclusion)
        {
            exclusion.Span(_line, _boxWidth, out lead, out span);

            // A row the exclusion spans edge to edge holds an empty line, and the text goes on below it.
            if (span <= 0)
            {
                _lineEnd = _index;
                _nextLine = _index;

                return;
            }
        }

        bool wrapping = _wrap == TextWrap.Word && _boxWidth > 0;

        int pen = 0;
        int width = 0;
        int previous = -1;

        // The last space on the line: where the line would end, where the next would begin, and the width
        // without the space, because an undrawn trailing space measures nothing.
        int spaceAt = -1;
        int spaceNext = -1;
        int widthAtSpace = 0;

        int cursor = _index;
        while (cursor < _text.Length)
        {
            int start = pen;
            bool drawn = TryAdvance(cursor, previous, ref start, out int codepoint, out Glyph glyph, out int consumed);

            if (codepoint == '\n')
            {
                _lineEnd = cursor;
                _nextLine = cursor + consumed;

                break;
            }

            if (!drawn)
            {
                cursor += consumed;

                continue;
            }

            // Taken before the overflow check. A space that overflows the box then becomes the break. Taken
            // after, it would open a line that the following word overflows in turn.
            if (codepoint == ' ')
            {
                spaceAt = cursor;
                spaceNext = cursor + consumed;
                widthAtSpace = width;
            }

            // Checked only past the line's first glyph. A glyph wider than the box has to go somewhere,
            // and breaking before it would place nothing and never advance.
            if (wrapping && previous >= 0 && start + glyph.XAdvance > span)
            {
                if (spaceAt >= 0)
                {
                    _lineEnd = spaceAt;
                    _nextLine = spaceNext;
                    width = widthAtSpace;
                }
                else
                {
                    _lineEnd = cursor;
                    _nextLine = cursor;
                }

                break;
            }

            pen = start + glyph.XAdvance;
            width = pen;
            previous = codepoint;
            cursor += consumed;
            _lineDraws = true;
        }

        if (_boxWidth > 0 && _alignment != HorizontalAlignment.Left)
        {
            int slack = span - width;
            _shift = _alignment == HorizontalAlignment.Center ? slack / 2 : slack;
        }

        _shift += lead;
    }

    // The codepoint at cursor and the UTF-16 units it spans. ASCII, which covers most runs, is answered
    // without decoding. Malformed UTF-16 decodes to the replacement character over one unit. A lone
    // surrogate reads as a codepoint the font has no glyph for and throws nothing.
    private readonly int CodepointAt(int cursor, out int consumed)
    {
        char unit = _text[cursor];
        if (unit < 0x80)
        {
            consumed = 1;

            return unit;
        }

        Rune.DecodeFromUtf16(_text[cursor..], out Rune rune, out consumed);

        return rune.Value;
    }

    // The step both walks take over a codepoint: read it, find its glyph, and kern the pen against the
    // codepoint drawn before it. Returns false for a break, a \r, or a codepoint the font has no glyph
    // for, and then leaves pen where it was so the pair either side still kerns. codepoint and consumed
    // are set either way.
    private readonly bool TryAdvance(int cursor, int previous, ref int pen, out int codepoint, out Glyph glyph, out int consumed)
    {
        codepoint = CodepointAt(cursor, out consumed);
        glyph = default;

        if (codepoint is '\n' or '\r' || !_font.TryGetGlyph(codepoint, out glyph))
        {
            return false;
        }

        if (previous >= 0)
        {
            pen += _font.GetKerning(previous, codepoint);
        }

        return true;
    }

    // The widest line and the line count of a run laid out this way, in font pixels, over the window's lines.
    // Lines counts from firstLine to the last line in the window that draws. Measuring walks the
    // placements, and a caller is told what the same layout draws.
    internal static (int Width, int Lines) Extent(
        BitmapFont font,
        ReadOnlySpan<char> text,
        int boxWidth,
        TextWrap wrap,
        int firstLine = 0,
        int? maxLines = null,
        TextExclusion? exclusion = null)
    {
        int width = 0;
        int lines = 0;

        foreach (GlyphPlacement placed in new GlyphRun(font, text, boxWidth, wrap, HorizontalAlignment.Left, firstLine, maxLines, exclusion))
        {
            width = Math.Max(width, placed.PenX + placed.Glyph.XAdvance);
            lines = Math.Max(lines, placed.Line - firstLine + 1);
        }

        return (width, lines);
    }
}

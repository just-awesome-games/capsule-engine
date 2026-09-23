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

    // The half-open char range of the line being emitted, and where the next line begins. The gap
    // between _lineEnd and _nextLine holds what the break consumed, a newline or the space a wrap broke
    // at.
    private int _lineEnd;
    private int _nextLine;
    private bool _lineOpen;

    // Font pixels this line is shifted right by to satisfy _alignment.
    private int _shift;

    private int _index;
    private int _codepoint;
    private int _pen;
    private int _line;

    // The last codepoint drawn on this line, or -1 at the start of a line. A codepoint the font has no
    // glyph for leaves it alone, so the pair either side of it still kerns.
    private int _previous;

    /// <summary>
    /// Lays <paramref name="text"/> out in <paramref name="font"/> as one left-aligned block, with
    /// each line as long as the text makes it.
    /// </summary>
    /// <param name="font">The font the run is laid out in.</param>
    /// <param name="text">
    /// The text to lay out. <c>\n</c> starts a new line, <c>\r</c> is ignored, and a codepoint the
    /// font carries no glyph for draws nothing and advances nothing.
    /// </param>
    public GlyphRun(BitmapFont font, ReadOnlySpan<char> text)
        : this(font, text, 0, TextWrap.None, HorizontalAlignment.Left)
    {
    }

    /// <summary>Lays <paramref name="text"/> out inside a box of <paramref name="boxWidth"/>.</summary>
    /// <param name="font">The font the run is laid out in.</param>
    /// <param name="text">The text to lay out, with the line rules the other constructor states.</param>
    /// <param name="boxWidth">
    /// The box's width in font pixels, which lines wrap inside and are aligned within. Zero or less
    /// means no box, and lines then neither wrap nor shift whatever the other two arguments say.
    /// </param>
    /// <param name="wrap">Whether a line too wide for the box breaks inside it.</param>
    /// <param name="alignment">Where each line sits between the box's edges.</param>
    public GlyphRun(BitmapFont font, ReadOnlySpan<char> text, int boxWidth, TextWrap wrap, HorizontalAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(font);

        _font = font;
        _text = text;
        _boxWidth = boxWidth;
        _wrap = wrap;
        _alignment = alignment;
        _previous = -1;
    }

    /// <summary>The glyph this enumerator has reached.</summary>
    public GlyphPlacement Current { get; private set; }

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
                OpenLine();
            }

            if (_index >= _lineEnd)
            {
                // A break's codepoints are counted but never drawn, so Index stays the position in the
                // text whatever the layout did with the character.
                while (_index < _nextLine)
                {
                    CodepointAt(_index, out int skipped);
                    _index += skipped;
                    _codepoint++;
                }

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

    // Finds where the line starting at _index ends and how far it is shifted. This is one measuring walk
    // per line, so laying out a run costs two passes over it.
    private void OpenLine()
    {
        _lineOpen = true;
        _lineEnd = _text.Length;
        _nextLine = _text.Length;
        _shift = 0;

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
            if (wrapping && previous >= 0 && start + glyph.XAdvance > _boxWidth)
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
        }

        if (_boxWidth > 0 && _alignment != HorizontalAlignment.Left)
        {
            int slack = _boxWidth - width;
            _shift = _alignment == HorizontalAlignment.Center ? slack / 2 : slack;
        }
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

    // The widest line and the line count of a run laid out this way, in font pixels. Measuring walks the
    // placements, and a caller is told what the same layout draws.
    internal static (int Width, int Lines) Extent(BitmapFont font, ReadOnlySpan<char> text, int boxWidth, TextWrap wrap)
    {
        int width = 0;
        int lines = 0;

        foreach (GlyphPlacement placed in new GlyphRun(font, text, boxWidth, wrap, HorizontalAlignment.Left))
        {
            width = Math.Max(width, placed.PenX + placed.Glyph.XAdvance);
            lines = Math.Max(lines, placed.Line + 1);
        }

        return (width, lines);
    }
}

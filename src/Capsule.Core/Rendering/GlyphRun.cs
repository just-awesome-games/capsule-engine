using System.Text;

namespace Capsule.Rendering;

/// <summary>
/// Where one glyph of a laid-out run sits, in font pixels from the run's origin — the top-left
/// corner of its first line.
/// </summary>
/// <param name="Glyph">The glyph to draw.</param>
/// <param name="PenX">
/// Font pixels right from the origin to this glyph's pen, alignment included;
/// <see cref="Rendering.Glyph.XOffset"/> still applies on top of it.
/// </param>
/// <param name="Line">Lines below the first, so the glyph's top edge is this times <see cref="BitmapFont.LineHeight"/>.</param>
/// <param name="Index">
/// The glyph's codepoint position in the run's text, counting every codepoint the text carries —
/// line breaks, codepoints the font has no glyph for, and the spaces a wrap broke at included. Rises
/// over the run and skips the codepoints that draw nothing.
/// </param>
public readonly record struct GlyphPlacement(Glyph Glyph, int PenX, int Line, int Index);

/// <summary>
/// The one layout pass over a font and a run of text, in font pixels and free of any texture, frame
/// or camera: <c>foreach</c> it to place every glyph the run draws, in reading order. Measuring and
/// drawing both enumerate this, so what a game measures is what gets drawn.
/// <para>
/// Allocation-free and it never throws: it is on the frame path. A consumer that draws its own
/// glyphs — rich text, a per-character animation — lays the run out here and emits whatever sprites
/// it likes onto either of a <see cref="FrameView"/>'s lists.
/// </para>
/// </summary>
public ref struct GlyphRun
{
    private readonly BitmapFont _font;
    private readonly ReadOnlySpan<char> _text;
    private readonly int _boxWidth;
    private readonly TextWrap _wrap;
    private readonly HorizontalAlignment _alignment;

    // The half-open char range of the line being emitted, and where the line after it begins. The
    // gap between _lineEnd and _nextLine is what the break consumed: a newline, or the space a wrap
    // broke at.
    private int _lineEnd;
    private int _nextLine;
    private bool _lineOpen;

    // Font pixels this line is shifted right by to satisfy _alignment.
    private int _shift;

    private int _index;
    private int _codepoint;
    private int _pen;
    private int _line;

    // The last codepoint drawn on this line, or -1 at the start of one. A codepoint the font has no
    // glyph for leaves it alone, so the pair either side of it still kerns.
    private int _previous;

    /// <summary>
    /// Lays <paramref name="text"/> out in <paramref name="font"/> as one left-aligned block of
    /// lines as long as the text makes them: the plain run.
    /// </summary>
    /// <param name="font">The font the run is laid out in; never null.</param>
    /// <param name="text">The text to lay out. <c>\n</c> starts a new line, <c>\r</c> is ignored, and a codepoint the font carries no glyph for draws nothing and advances nothing.</param>
    public GlyphRun(BitmapFont font, ReadOnlySpan<char> text)
        : this(font, text, 0, TextWrap.None, HorizontalAlignment.Left)
    {
    }

    /// <summary>Lays <paramref name="text"/> out inside a box of <paramref name="boxWidth"/>.</summary>
    /// <param name="font">The font the run is laid out in; never null.</param>
    /// <param name="text">The text to lay out, with the line rules the other constructor states.</param>
    /// <param name="boxWidth">
    /// The box's width in font pixels, which lines wrap inside and are aligned within. Zero or less
    /// is no box at all: lines neither wrap nor shift, whatever the other two arguments say.
    /// </param>
    /// <param name="wrap">Whether a line too wide for the box breaks inside it.</param>
    /// <param name="alignment">Where each line sits between the box's edges.</param>
    public GlyphRun(BitmapFont font, ReadOnlySpan<char> text, int boxWidth, TextWrap wrap, HorizontalAlignment alignment)
    {
        _font = font;
        _text = text;
        _boxWidth = boxWidth;
        _wrap = wrap;
        _alignment = alignment;
        _previous = -1;
    }

    /// <summary>The glyph this enumerator has reached.</summary>
    public GlyphPlacement Current { get; private set; }

    /// <summary>The run itself, so <c>foreach</c> walks a copy and leaves this one where it was.</summary>
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
                // The break's own codepoints are counted, never drawn, so an Index stays the
                // position in the text whatever the layout did with the character.
                while (_index < _nextLine)
                {
                    Rune.DecodeFromUtf16(_text[_index..], out _, out int skipped);
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

            // Malformed UTF-16 decodes to the replacement character and consumes one unit, so a
            // lone surrogate is a codepoint the font has no glyph for rather than a throw.
            Rune.DecodeFromUtf16(_text[_index..], out Rune rune, out int consumed);
            _index += consumed;
            int index = _codepoint++;

            if (rune.Value == '\r' || !_font.TryGetGlyph(rune.Value, out Glyph glyph))
            {
                continue;
            }

            if (_previous >= 0)
            {
                _pen += _font.GetKerning(_previous, rune.Value);
            }

            Current = new GlyphPlacement(glyph, _pen + _shift, _line, index);
            _pen += glyph.XAdvance;
            _previous = rune.Value;

            return true;
        }

        return false;
    }

    // Finds where the line starting at _index ends and how far it is shifted. One measuring walk per
    // line, so laying a run out costs two passes over it rather than one.
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

        // The last space on the line: where the line would end, where the one after it would begin,
        // and the width without the space, since a trailing space no one draws measures nothing.
        int spaceAt = -1;
        int spaceNext = -1;
        int widthAtSpace = 0;

        int cursor = _index;
        while (cursor < _text.Length)
        {
            Rune.DecodeFromUtf16(_text[cursor..], out Rune rune, out int consumed);

            if (rune.Value == '\n')
            {
                _lineEnd = cursor;
                _nextLine = cursor + consumed;

                break;
            }

            if (rune.Value == '\r' || !_font.TryGetGlyph(rune.Value, out Glyph glyph))
            {
                cursor += consumed;

                continue;
            }

            int start = pen + (previous >= 0 ? _font.GetKerning(previous, rune.Value) : 0);

            // Taken before the overflow check, so a space that overflows the box is itself the break
            // rather than opening a line the word after it then overflows in turn.
            if (rune.Value == ' ')
            {
                spaceAt = cursor;
                spaceNext = cursor + consumed;
                widthAtSpace = width;
            }

            // Only past the first glyph of the line: a glyph wider than the whole box still has to
            // go somewhere, and breaking before it would place nothing and never advance.
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
            previous = rune.Value;
            cursor += consumed;
        }

        if (_boxWidth > 0 && _alignment != HorizontalAlignment.Left)
        {
            int slack = _boxWidth - width;
            _shift = _alignment == HorizontalAlignment.Center ? slack / 2 : slack;
        }
    }

    // The widest line and the line count of a run laid out this way, in font pixels. Walking the
    // placements is what measures: what a caller is told is what the same layout draws.
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

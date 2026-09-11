using System.Text;

namespace Capsule.Rendering;

// Where one glyph of a run sits, in font pixels from the run's origin — the top-left corner of its
// first line.
internal readonly record struct GlyphPlacement(Glyph Glyph, int PenX, int Line);

// The one layout pass over (font, text), in font pixels and free of any texture, frame or camera.
// Measuring and drawing both enumerate this, so what a game measures is what it gets drawn.
// Allocation-free, and it never throws: it is on the frame path.
internal ref struct GlyphRun(BitmapFont font, ReadOnlySpan<char> text)
{
    private readonly ReadOnlySpan<char> _text = text;

    private int _index;
    private int _pen;
    private int _line;

    // The last codepoint drawn on this line, or -1 at the start of one. A codepoint the font has no
    // glyph for leaves it alone, so the pair either side of it still kerns.
    private int _previous = -1;

    public GlyphPlacement Current { get; private set; }

    // Public on an internal type: foreach binds only to a public GetEnumerator. The run is its own
    // enumerator, and foreach takes the copy this hands back.
    public readonly GlyphRun GetEnumerator() => this;

    public bool MoveNext()
    {
        while (_index < _text.Length)
        {
            // Malformed UTF-16 decodes to the replacement character and consumes one unit, so a
            // lone surrogate is a codepoint the font has no glyph for rather than a throw.
            Rune.DecodeFromUtf16(_text[_index..], out Rune rune, out int consumed);
            _index += consumed;

            if (rune.Value == '\n')
            {
                _line++;
                _pen = 0;
                _previous = -1;
                continue;
            }

            if (rune.Value == '\r' || !font.TryGetGlyph(rune.Value, out Glyph glyph))
            {
                continue;
            }

            if (_previous >= 0)
            {
                _pen += font.GetKerning(_previous, rune.Value);
            }

            Current = new GlyphPlacement(glyph, _pen, _line);
            _pen += glyph.XAdvance;
            _previous = rune.Value;

            return true;
        }

        return false;
    }
}

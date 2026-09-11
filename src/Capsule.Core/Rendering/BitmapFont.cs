using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// A font baked to texture pages: its line metrics, the glyph cut for each codepoint it carries,
/// and the kerning between them. Immutable once constructed, and built by generated code from the
/// data the build read out of the authored font, so nothing is parsed or measured at run time.
/// Every measure is in font pixels — the units the font was baked at.
/// <para>
/// A run of text is drawn by handing a <see cref="TextIntent"/> to
/// <see cref="FrameView.Add(in TextIntent)"/>, which expands it into one sprite per glyph; a
/// <c>Label</c> does that for an entity.
/// </para>
/// </summary>
public sealed class BitmapFont
{
    // Codepoints below this get a direct index; the rest binary-search the sorted glyphs. Covers
    // ASCII, which is every glyph of most Latin fonts and the hot path of the rest.
    private const int DenseLimit = 128;

    private readonly TextureHandle[] _pages;

    // Ascending by codepoint, and _codepoints is the key each glyph sorted by.
    private readonly Glyph[] _glyphs;
    private readonly int[] _codepoints;

    // Ascending by the pair packed into one key: the first codepoint above the second.
    private readonly KerningPair[] _kernings;
    private readonly long[] _pairs;

    // Index into _glyphs per codepoint below DenseLimit; -1 where the font has no glyph.
    private readonly int[] _dense;

    /// <summary>Builds a font from the data the build read. Every array is copied.</summary>
    /// <param name="lineHeight">Font pixels from one line's top edge to the next; positive.</param>
    /// <param name="baseline">Font pixels from a line's top edge down to the baseline.</param>
    /// <param name="pages">The texture pages glyphs are cut from, in the order the font declares them; at least one.</param>
    /// <param name="glyphs">Every glyph the font carries, in any order; each on a page this font declares, and no codepoint twice.</param>
    /// <param name="kernings">The kerning pairs the font carries, in any order; may be empty.</param>
    /// <exception cref="ArgumentNullException">An array argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lineHeight"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pages"/> is empty, a glyph names no page of this font, or two glyphs carry
    /// one codepoint.
    /// </exception>
    public BitmapFont(int lineHeight, int baseline, TextureHandle[] pages, Glyph[] glyphs, KerningPair[] kernings)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(kernings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);

        if (pages.Length == 0)
        {
            throw new ArgumentException("A font carries at least one page.", nameof(pages));
        }

        LineHeight = lineHeight;
        Base = baseline;
        _pages = [.. pages];
        _glyphs = [.. glyphs];
        _kernings = [.. kernings];

        _codepoints = new int[_glyphs.Length];
        for (int i = 0; i < _glyphs.Length; i++)
        {
            _codepoints[i] = _glyphs[i].Codepoint;
        }

        _pairs = new long[_kernings.Length];
        for (int i = 0; i < _kernings.Length; i++)
        {
            _pairs[i] = Pair(_kernings[i].First, _kernings[i].Second);
        }

        Array.Sort(_codepoints, _glyphs);
        Array.Sort(_pairs, _kernings);

        _dense = new int[DenseLimit];
        Array.Fill(_dense, -1);

        for (int i = 0; i < _glyphs.Length; i++)
        {
            Glyph glyph = _glyphs[i];

            if (glyph.Page < 0 || glyph.Page >= _pages.Length)
            {
                throw new ArgumentException(
                    $"Glyph {glyph.Codepoint} names page {glyph.Page}, and this font carries {_pages.Length}.",
                    nameof(glyphs));
            }

            if (i > 0 && _codepoints[i - 1] == glyph.Codepoint)
            {
                throw new ArgumentException(
                    $"Codepoint {glyph.Codepoint} is carried by two glyphs.",
                    nameof(glyphs));
            }

            if (glyph.Codepoint is >= 0 and < DenseLimit)
            {
                _dense[glyph.Codepoint] = i;
            }
        }
    }

    /// <summary>Font pixels from one line's top edge to the next; always positive.</summary>
    public int LineHeight { get; }

    /// <summary>Font pixels from a line's top edge down to its baseline.</summary>
    public int Base { get; }

    /// <summary>
    /// The texture pages this font's glyphs are cut from, in the order the font declares them;
    /// <see cref="Glyph.Page"/> indexes into this. Ships under <c>assets/fonts/</c>.
    /// </summary>
    public ReadOnlySpan<TextureHandle> Pages => _pages;

    /// <summary>The glyph cut for <paramref name="codepoint"/>, if this font carries one.</summary>
    /// <param name="codepoint">A Unicode scalar value.</param>
    /// <param name="glyph">The glyph, or the default where the font carries none.</param>
    /// <returns>Whether the font carries a glyph for it.</returns>
    public bool TryGetGlyph(int codepoint, out Glyph glyph)
    {
        int found = codepoint is >= 0 and < DenseLimit
            ? _dense[codepoint]
            : _codepoints.AsSpan().BinarySearch(codepoint);

        if (found < 0)
        {
            glyph = default;

            return false;
        }

        glyph = _glyphs[found];

        return true;
    }

    /// <summary>
    /// Font pixels this font adds to the pen between two codepoints drawn in this order on one
    /// line; zero where it carries no pair for them.
    /// </summary>
    /// <param name="first">The codepoint drawn first.</param>
    /// <param name="second">The codepoint drawn immediately after it.</param>
    public int GetKerning(int first, int second)
    {
        int found = _pairs.AsSpan().BinarySearch(Pair(first, second));

        return found < 0 ? 0 : _kernings[found].Amount;
    }

    /// <summary>
    /// The extent <paramref name="text"/> occupies in font pixels, laid out exactly as
    /// <see cref="FrameView.Add(in TextIntent)"/> draws it: X is the furthest right any line's pen
    /// reached after drawing a glyph, kerning included, and Y runs from the run's first line down to
    /// the bottom edge of the last line that draws one, in whole <see cref="LineHeight"/> steps — so
    /// a leading blank line adds to the height and a trailing one does not. Text that draws nothing
    /// measures zero. Multiply by a <see cref="TextIntent.Scale"/> to get world units.
    /// </summary>
    /// <param name="text">The text to measure; the same line rules a drawn run follows.</param>
    public Vector2 Measure(ReadOnlySpan<char> text)
    {
        int width = 0;
        int lines = 0;

        foreach (GlyphPlacement placed in new GlyphRun(this, text))
        {
            width = Math.Max(width, placed.PenX + placed.Glyph.XAdvance);
            lines = Math.Max(lines, placed.Line + 1);
        }

        return new Vector2(width, lines * LineHeight);
    }

    // One ordered pair as one sortable key, so a pair is found by an ordinary binary search over
    // longs rather than a hand-rolled two-field one. The second codepoint is biased into the
    // unsigned half it occupies, so the key orders by (first, second) as signed integers.
    private static long Pair(int first, int second) => ((long)first << 32) | ((uint)second ^ 0x8000_0000u);
}

using System.ComponentModel;
using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// A font baked to texture pages, with its line metrics, the glyph cut for each codepoint it
/// carries, and the kerning between them. Generated code builds it from the data the build read out
/// of the authored font, and it is immutable after that.
/// </summary>
/// <remarks>
/// Every measure is in font pixels, the units the font was baked at. To draw a run of text, hand a
/// <see cref="TextIntent"/> to <see cref="FrameView.Add(in TextIntent)"/>. A <c>Label</c> does that
/// for an entity.
/// </remarks>
public sealed partial class BitmapFont
{
    // Codepoints below this get a direct index, and the rest binary-search the sorted glyphs. This covers
    // ASCII, which is every glyph of most Latin fonts and the hot path of the rest.
    private const int DenseLimit = 128;

    private readonly TextureHandle[] _pages;

    // Ascending by codepoint. _codepoints holds the sort key for each glyph.
    private readonly Glyph[] _glyphs;
    private readonly int[] _codepoints;

    // Ascending by the packed pair key, with the first codepoint in the high half.
    private readonly KerningPair[] _kernings;
    private readonly long[] _pairs;

    // Index into _glyphs per codepoint below DenseLimit, and -1 where the font has no glyph.
    private readonly int[] _dense;

    /// <summary>Builds a font from the data the build read.</summary>
    /// <remarks>Called by generated code. Every array is copied.</remarks>
    /// <param name="lineHeight">Font pixels from one line's top edge to the next. Must be positive.</param>
    /// <param name="baseline">Font pixels from a line's top edge down to the baseline.</param>
    /// <param name="pages">The texture pages glyphs are cut from, in the order the font declares them. At least one.</param>
    /// <param name="glyphs">Every glyph the font carries, in any order. Each names a page this font declares, and no codepoint appears twice.</param>
    /// <param name="kernings">The kerning pairs the font carries, in any order. May be empty.</param>
    /// <exception cref="ArgumentNullException">An array argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lineHeight"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pages"/> is empty, a glyph names no page of this font, or two glyphs carry
    /// one codepoint.
    /// </exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
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
        Baseline = baseline;
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

    /// <summary>Font pixels from one line's top edge to the next. Always positive.</summary>
    public int LineHeight { get; }

    /// <summary>Font pixels from a line's top edge down to its baseline.</summary>
    public int Baseline { get; }

    /// <summary>
    /// The texture pages this font's glyphs are cut from, in the order the font declares them.
    /// </summary>
    /// <remarks>
    /// <see cref="Glyph.Page"/> indexes into this. A game's font pages are its own textures.
    /// <see cref="Default"/>'s page is engine-owned and ships with the runtime.
    /// </remarks>
    public ReadOnlySpan<TextureHandle> Pages => _pages;

    /// <summary>The glyph cut for <paramref name="codepoint"/>, if this font carries one.</summary>
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
    /// Font pixels this font adds to the pen between two codepoints drawn in this order on one line.
    /// Zero where the font carries no pair for them.
    /// </summary>
    public int GetKerning(int first, int second)
    {
        if (_pairs.Length == 0)
        {
            return 0;
        }

        int found = _pairs.AsSpan().BinarySearch(Pair(first, second));

        return found < 0 ? 0 : _kernings[found].Amount;
    }

    /// <summary>
    /// The extent <paramref name="text"/> occupies in font pixels, laid out as
    /// <see cref="FrameView.Add(in TextIntent)"/> draws it. X is the furthest right any line's pen
    /// reached after drawing a glyph, kerning included.
    /// </summary>
    /// <remarks>
    /// Y runs from the run's first line down to the bottom edge of the last line that draws a
    /// glyph, in whole <see cref="LineHeight"/> steps. A leading blank line adds to the height and
    /// a trailing one does not. Text that draws nothing measures zero. Multiply by a
    /// <see cref="TextIntent.Scale"/> to get world units.
    /// </remarks>
    public Vector2 Measure(ReadOnlySpan<char> text) => Measured(text, 0, TextWrap.None);

    /// <summary>
    /// The same extent once the text is word-wrapped into a box <paramref name="wrapWidth"/> font
    /// pixels wide, where zero or less wraps nothing. X is the widest line the wrap produced, which
    /// stays within the box unless a single glyph is wider than it.
    /// </summary>
    public Vector2 Measure(ReadOnlySpan<char> text, int wrapWidth) => Measured(text, wrapWidth, TextWrap.Word);

    private Vector2 Measured(ReadOnlySpan<char> text, int boxWidth, TextWrap wrap)
    {
        (int width, int lines) = GlyphRun.Extent(this, text, boxWidth, wrap);

        return new Vector2(width, lines * LineHeight);
    }

    // One ordered pair packed as one sortable key, found by a plain binary search over longs. The
    // second codepoint is biased into the unsigned half it occupies, so the key orders by (first, second)
    // as signed integers.
    private static long Pair(int first, int second) => ((long)first << 32) | ((uint)second ^ 0x8000_0000u);
}

using System.Collections.Immutable;

namespace Capsule.Text;

/// <summary>Lays strings out on the <see cref="PixelFont"/> grid.</summary>
public static class PixelText
{
    /// <summary>Grid columns a string of <paramref name="characterCount"/> glyphs spans.</summary>
    public static int MeasureWidth(int characterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterCount);

        return characterCount == 0
            ? 0
            : checked((characterCount * PixelFont.GlyphWidth) + ((characterCount - 1) * PixelFont.GlyphSpacing));
    }

    /// <exception cref="ArgumentException">Some character in <paramref name="text"/> has no glyph.</exception>
    public static PixelTextLayout Layout(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<GridCell> cells = [];

        for (int index = 0; index < text.Length; index++)
        {
            ImmutableArray<string> rows = PixelFont.Rows(text[index]);
            int columnOffset = index * (PixelFont.GlyphWidth + PixelFont.GlyphSpacing);

            for (int row = 0; row < PixelFont.GlyphHeight; row++)
            {
                string bits = rows[row];
                for (int column = 0; column < PixelFont.GlyphWidth; column++)
                {
                    if (bits[column] == '#')
                    {
                        cells.Add(new GridCell(columnOffset + column, row));
                    }
                }
            }
        }

        int height = text.Length == 0 ? 0 : PixelFont.GlyphHeight;
        return new PixelTextLayout([.. cells], MeasureWidth(text.Length), height);
    }

    /// <summary>
    /// Top-left pixel position that centres <paramref name="layout"/> in a viewport,
    /// with each grid cell drawn <paramref name="cellPixels"/> pixels square.
    /// Truncates toward zero, so an odd leftover pixel falls on the right/bottom.
    /// </summary>
    public static PixelOrigin CenterOrigin(PixelTextLayout layout, int cellPixels, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellPixels);

        return new PixelOrigin(
            checked((viewportWidth - (layout.Width * cellPixels)) / 2),
            checked((viewportHeight - (layout.Height * cellPixels)) / 2));
    }
}

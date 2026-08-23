using Capsule.Text;

namespace Capsule.Tests.Text;

public sealed class PixelTextTests
{
    private const string Sample = "HELLO WORLD";

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(2, 11)]
    [InlineData(11, 65)]
    public void MeasureWidth_AddsSpacingBetweenGlyphsOnly(int characterCount, int expectedWidth)
    {
        Assert.Equal(expectedWidth, PixelText.MeasureWidth(characterCount));
    }

    [Fact]
    public void MeasureWidth_Throws_ForANegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelText.MeasureWidth(-1));
    }

    [Fact]
    public void MeasureWidth_Throws_WhenTheWidthOverflows()
    {
        Assert.Throws<OverflowException>(() => PixelText.MeasureWidth(int.MaxValue));
    }

    [Fact]
    public void Layout_OfTheMessage_HasExpectedDimensions()
    {
        PixelTextLayout layout = PixelText.Layout(Sample);

        Assert.Equal(65, layout.Width);
        Assert.Equal(PixelFont.GlyphHeight, layout.Height);
    }

    [Fact]
    public void Layout_OfEmptyText_IsEmpty()
    {
        PixelTextLayout layout = PixelText.Layout(string.Empty);

        Assert.Equal(0, layout.Width);
        Assert.Equal(0, layout.Height);
        Assert.Equal(0, layout.CellCount);
    }

    [Theory]
    [InlineData('H')]
    [InlineData('E')]
    [InlineData('L')]
    [InlineData('O')]
    [InlineData('W')]
    [InlineData('R')]
    [InlineData('D')]
    public void Layout_ProducesCells_ForEveryPrintableGlyph(char character)
    {
        Assert.True(PixelText.Layout(character.ToString()).CellCount > 0);
    }

    [Fact]
    public void Layout_OfASpace_ProducesNoCells()
    {
        Assert.Equal(0, PixelText.Layout(" ").CellCount);
    }

    [Fact]
    public void Layout_PlacesEveryCellInsideTheMeasuredBounds()
    {
        PixelTextLayout layout = PixelText.Layout(Sample);

        foreach (GridCell cell in layout.Cells)
        {
            Assert.InRange(cell.X, 0, layout.Width - 1);
            Assert.InRange(cell.Y, 0, layout.Height - 1);
        }
    }

    [Fact]
    public void Layout_OffsetsTheSecondGlyphByGlyphWidthPlusSpacing()
    {
        PixelTextLayout single = PixelText.Layout("L");
        PixelTextLayout doubled = PixelText.Layout("LL");
        int stride = PixelFont.GlyphWidth + PixelFont.GlyphSpacing;

        GridCell[] expectedSecondGlyph =
            [.. single.Cells.ToArray().Select(cell => new GridCell(cell.X + stride, cell.Y))];

        Assert.Equal(single.CellCount * 2, doubled.CellCount);
        Assert.Equal(expectedSecondGlyph, doubled.Cells.ToArray().Skip(single.CellCount));
    }

    [Fact]
    public void Layout_LeavesTheInterGlyphColumnEmpty()
    {
        PixelTextLayout layout = PixelText.Layout("LL");

        Assert.DoesNotContain(layout.Cells.ToArray(), cell => cell.X == PixelFont.GlyphWidth);
    }

    [Fact]
    public void Layout_Throws_ForAnUnsupportedCharacter()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => PixelText.Layout("HELLO, WORLD"));

        Assert.Contains("','", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_Throws_ForNullText()
    {
        Assert.Throws<ArgumentNullException>(() => PixelText.Layout(null!));
    }

    [Fact]
    public void CenterOrigin_CentresTheLayoutInTheViewport()
    {
        PixelTextLayout layout = PixelText.Layout(Sample);

        PixelOrigin origin = PixelText.CenterOrigin(layout, 8, 1280, 720);

        Assert.Equal((1280 - (65 * 8)) / 2, origin.X);
        Assert.Equal((720 - (7 * 8)) / 2, origin.Y);
    }

    [Fact]
    public void CenterOrigin_TruncatesTheOddPixelTowardZero()
    {
        PixelTextLayout layout = PixelText.Layout("L");

        PixelOrigin origin = PixelText.CenterOrigin(layout, 1, 6, 8);

        Assert.Equal(0, origin.X);
        Assert.Equal(0, origin.Y);
    }

    [Fact]
    public void CenterOrigin_GoesNegative_WhenTheLayoutOverflowsTheViewport()
    {
        PixelTextLayout layout = PixelText.Layout(Sample);

        PixelOrigin origin = PixelText.CenterOrigin(layout, 8, 320, 240);

        Assert.True(origin.X < 0);
    }

    [Fact]
    public void CenterOrigin_Throws_ForANonPositiveCellSize()
    {
        PixelTextLayout layout = PixelText.Layout("L");

        Assert.Throws<ArgumentOutOfRangeException>(() => PixelText.CenterOrigin(layout, 0, 100, 100));
    }

    [Fact]
    public void CenterOrigin_Throws_WhenTheScaledSizeOverflows()
    {
        PixelTextLayout layout = PixelText.Layout(Sample);

        Assert.Throws<OverflowException>(() => PixelText.CenterOrigin(layout, int.MaxValue, 100, 100));
    }

    [Fact]
    public void CenterOrigin_Throws_ForANullLayout()
    {
        Assert.Throws<ArgumentNullException>(() => PixelText.CenterOrigin(null!, 8, 100, 100));
    }
}

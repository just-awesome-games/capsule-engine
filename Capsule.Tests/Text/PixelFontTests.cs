using System.Collections.Immutable;
using Capsule.Text;

namespace Capsule.Tests.Text;

public sealed class PixelFontTests
{
    public static TheoryData<char> SupportedGlyphs =>
        [' ', 'H', 'E', 'L', 'O', 'W', 'R', 'D'];

    [Theory]
    [MemberData(nameof(SupportedGlyphs))]
    public void Supports_ReportsTrue_ForEveryDefinedGlyph(char character)
    {
        Assert.True(PixelFont.Supports(character));
    }

    [Theory]
    [MemberData(nameof(SupportedGlyphs))]
    public void Rows_AreGlyphSized_ForEverySupportedGlyph(char character)
    {
        ImmutableArray<string> rows = PixelFont.Rows(character);

        Assert.Equal(PixelFont.GlyphHeight, rows.Length);
        Assert.All(rows, row => Assert.Equal(PixelFont.GlyphWidth, row.Length));
        Assert.All(rows, row => Assert.All(row, cell => Assert.True(cell is '#' or '.')));
    }

    [Theory]
    [InlineData('H')]
    [InlineData('E')]
    [InlineData('L')]
    [InlineData('O')]
    [InlineData('W')]
    [InlineData('R')]
    [InlineData('D')]
    public void Rows_ContainFilledCells_ForEveryPrintableGlyph(char character)
    {
        Assert.Contains(PixelFont.Rows(character), row => row.Contains('#', StringComparison.Ordinal));
    }

    [Fact]
    public void Space_IsBlank()
    {
        Assert.All(PixelFont.Rows(' '), row => Assert.False(row.Contains('#', StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData('A')]
    [InlineData('h')]
    [InlineData('!')]
    [InlineData('\n')]
    public void Supports_ReportsFalse_ForUndefinedCharacters(char character)
    {
        Assert.False(PixelFont.Supports(character));
    }

    [Fact]
    public void Rows_Throws_ForAnUndefinedCharacter()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => PixelFont.Rows('Z'));

        Assert.Equal("character", error.ParamName);
    }

    [Fact]
    public void SupportedCharacters_AreExactlyTheDefinedGlyphs()
    {
        char[] expected = [' ', 'D', 'E', 'H', 'L', 'O', 'R', 'W'];

        Assert.Equal(expected, PixelFont.SupportedCharacters.Order());
    }
}

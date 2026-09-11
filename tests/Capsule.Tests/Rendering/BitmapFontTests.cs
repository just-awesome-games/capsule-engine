using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// The font's own contracts: what it refuses to be built from, what it looks up, and the one layout
// pass that measuring and drawing share.
public sealed class BitmapFontTests
{
    [Fact]
    public void AGlyph_IsFoundWhetherItIsAsciiOrAstral()
    {
        BitmapFont font = FontFixtures.Font();

        Assert.True(font.TryGetGlyph('A', out Glyph ascii));
        Assert.Equal(FontFixtures.A, ascii);
        Assert.True(font.TryGetGlyph(FontFixtures.Grin, out Glyph astral));
        Assert.Equal(FontFixtures.Emoji, astral);

        Assert.False(font.TryGetGlyph('C', out Glyph missing));
        Assert.Equal(default, missing);
        Assert.False(font.TryGetGlyph(0x1F601, out _));
    }

    [Fact]
    public void Kerning_IsTheDeclaredPairsAmountAndOtherwiseNothing()
    {
        BitmapFont font = FontFixtures.Font();

        Assert.Equal(-2, font.GetKerning('A', 'B'));

        // Ordered: the reverse pair is a different pair, not the same one.
        Assert.Equal(0, font.GetKerning('B', 'A'));
        Assert.Equal(0, font.GetKerning('A', 'A'));
    }

    [Fact]
    public void TwoGlyphsOfOneCodepoint_AreRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new BitmapFont(10, 8, [FontFixtures.Page], [FontFixtures.A, FontFixtures.A], []));

        Assert.Equal("glyphs", error.ParamName);
    }

    [Fact]
    public void AGlyphOnAPageTheFontDoesNotCarry_IsRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new BitmapFont(10, 8, [FontFixtures.Page], [FontFixtures.A with { Page = 1 }], []));

        Assert.Equal("glyphs", error.ParamName);
    }

    [Fact]
    public void AFontWithNoPage_IsRefused()
    {
        Assert.Equal(
            "pages",
            Assert.Throws<ArgumentException>(() => new BitmapFont(10, 8, [], [], [])).ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ALineThatIsNotAtLeastOnePixelTall_IsRefused(int lineHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BitmapFont(lineHeight, 8, [FontFixtures.Page], [FontFixtures.A], []));
    }

    // The pen carries the previous glyph's advance plus the pair's kerning, and nothing else.
    [Fact]
    public void ARun_AdvancesByEachGlyphAndKernsBetweenThePairsItCarries()
    {
        Assert.Equal([(FontFixtures.A, 0, 0), (FontFixtures.B, 3, 0)], Placements("AB"));

        // No pair declared for B against A, so the advance stands alone.
        Assert.Equal([(FontFixtures.B, 0, 0), (FontFixtures.A, 6, 0)], Placements("BA"));
    }

    [Fact]
    public void ANewline_StartsTheNextLineAtThePenOrigin()
    {
        Assert.Equal([(FontFixtures.A, 0, 0), (FontFixtures.B, 0, 1)], Placements("A\nB"));
    }

    [Fact]
    public void ACarriageReturn_IsIgnoredRatherThanBreakingTheLine()
    {
        Assert.Equal(Placements("A\nB"), Placements("A\r\nB"));
        Assert.Equal(Placements("AB"), Placements("A\rB"));
    }

    // A codepoint the font has no glyph for is drawn as nothing and advances nothing, so the pair
    // either side of it still kerns.
    [Fact]
    public void ACodepointTheFontDoesNotCarry_DrawsNothingAndAdvancesNothing()
    {
        Assert.Equal(Placements("AB"), Placements("A§B"));
    }

    [Fact]
    public void ASurrogatePair_IsOneCodepointAndOneGlyph()
    {
        Assert.Equal([(FontFixtures.Emoji, 0, 0), (FontFixtures.A, 9, 0)], Placements("\U0001F600A"));
    }

    // Malformed UTF-16 is a codepoint the font has no glyph for, never a throw: this is the frame
    // path.
    [Fact]
    public void ALoneSurrogate_DrawsNothing()
    {
        Assert.Equal([(FontFixtures.A, 0, 0)], Placements("\ud83dA"));
    }

    [Fact]
    public void Measure_IsTheWidestLinesPenAndTheLinesThatDraw()
    {
        BitmapFont font = FontFixtures.Font();

        // "AB" ends at 3 + 6; "A" at 5.
        Assert.Equal(new Vector2(9f, FontFixtures.LineHeight * 2), font.Measure("AB\nA"));
        Assert.Equal(new Vector2(5f, FontFixtures.LineHeight), font.Measure("A"));
        Assert.Equal(Vector2.Zero, font.Measure(string.Empty));
        Assert.Equal(Vector2.Zero, font.Measure("§"));
    }

    private static (Glyph Glyph, int PenX, int Line)[] Placements(string text)
    {
        List<(Glyph, int, int)> placed = [];

        foreach (GlyphPlacement placement in new GlyphRun(FontFixtures.Font(), text))
        {
            placed.Add((placement.Glyph, placement.PenX, placement.Line));
        }

        return [.. placed];
    }
}

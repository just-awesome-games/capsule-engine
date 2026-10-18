using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class DefaultBitmapFontTests
{
    // Printable ASCII and the Latin-1 Supplement, 191 glyphs, and no other codepoint.
    [Fact]
    public void Default_CarriesExactlyAsciiAndLatin1SupplementGlyphs()
    {
        BitmapFont font = BitmapFont.Default;
        int[] expected = [.. Enumerable.Range(32, 95).Concat(Enumerable.Range(160, 96))];
        List<int> carried = [];

        for (int codepoint = 0; codepoint <= 0x10FFFF; codepoint++)
        {
            if (font.TryGetGlyph(codepoint, out _))
            {
                carried.Add(codepoint);
            }
        }

        Assert.Equal(expected, carried);
    }

    // Glyphs sit in 8 by 16 cells of the one page, sixteen to a row in codepoint order, with no offset
    // and an advance of their width.
    [Theory]
    [InlineData(0x20, 0, 0)]
    [InlineData(0x2F, 120, 0)]
    [InlineData(0x30, 0, 16)]
    [InlineData(0x7E, 112, 80)]
    [InlineData(0xA0, 120, 80)]
    [InlineData(0xFF, 112, 176)]
    public void Default_LaysEachGlyphInItsBakedPageCell(int codepoint, int x, int y)
    {
        BitmapFont font = BitmapFont.Default;

        Assert.Same(font, BitmapFont.Default);
        Assert.Equal(16, font.LineHeight);
        Assert.Equal(12, font.Baseline);
        Assert.Equal(0, font.GetKerning('A', 'V'));
        Assert.True(font.TryGetGlyph(codepoint, out Glyph glyph));
        Assert.Equal(new Glyph(codepoint, Page: 0, new TextureRegion(x, y, 8, 16), XOffset: 0, YOffset: 0, XAdvance: 8), glyph);
    }

    [Fact]
    public void Default_UsesOneEngineOwnedPageThatAssetCollectionIgnores()
    {
        TextureHandle page = Assert.Single(BitmapFont.Default.Pages.ToArray());

        Assert.True(page.IsEngineOwned);

        AssetCollection assets = new();
        assets.Add(page);

        Assert.Empty(assets.Textures);
    }

    [Fact]
    public void AFrameView_ExpandsDefaultFontTextToOneSpritePerVisibleCharacter()
    {
        FrameView view = new();
        view.Add(new TextIntent(
            BitmapFont.Default,
            "A\u00E9",
            Vector2.Zero,
            Vector2.Zero,
            Vector2.One,
            ColorRgba.White));

        Assert.Equal(2, view.Sprites.Length);
        Assert.Equal(new RenderMetrics(Submitted: 2, Visible: 2), view.Metrics);
        Assert.All(view.Sprites.ToArray(), sprite => Assert.Equal(BitmapFont.Default.Pages[0], sprite.Sprite.Texture));
    }
}

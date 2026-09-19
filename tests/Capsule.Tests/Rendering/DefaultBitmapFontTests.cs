using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class DefaultBitmapFontTests
{
    [Fact]
    public void Default_CarriesExactlyAsciiAndLatin1SupplementGlyphs()
    {
        BitmapFont font = BitmapFont.Default;
        HashSet<int> expected = [.. ExpectedCodepoints()];
        int found = 0;

        for (int codepoint = 0; codepoint <= 0x10FFFF; codepoint++)
        {
            if (font.TryGetGlyph(codepoint, out _))
            {
                Assert.Contains(codepoint, expected);
                found++;
            }
        }

        foreach (int codepoint in expected)
        {
            Assert.True(font.TryGetGlyph(codepoint, out _));
        }

        Assert.Equal(191, found);
    }

    [Fact]
    public void Default_HasTheBakedMetricsAndSixteenByEightPageCells()
    {
        BitmapFont font = BitmapFont.Default;

        Assert.Same(font, BitmapFont.Default);
        Assert.Equal(16, font.LineHeight);
        Assert.Equal(12, font.Baseline);

        int index = 0;
        foreach (int codepoint in ExpectedCodepoints())
        {
            Assert.True(font.TryGetGlyph(codepoint, out Glyph glyph));
            Assert.Equal(0, glyph.Page);
            Assert.Equal(
                new TextureRegion((index % 16) * 8, (index / 16) * 16, 8, 16),
                glyph.Region);
            Assert.Equal(0, glyph.XOffset);
            Assert.Equal(0, glyph.YOffset);
            Assert.Equal(8, glyph.XAdvance);
            Assert.True(glyph.Region.X >= 0 && glyph.Region.Y >= 0);
            Assert.True(glyph.Region.X + glyph.Region.Width <= 128);
            Assert.True(glyph.Region.Y + glyph.Region.Height <= 192);
            index++;
        }

        Assert.Equal(191, index);
        Assert.Equal(0, font.GetKerning('A', 'V'));
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
            ColorRgba.White)
        {
            VisibleCharacters = 2,
        });

        Assert.Equal(2, view.Sprites.Length);
        Assert.Equal(new RenderMetrics(Submitted: 2, Visible: 2), view.Metrics);
        Assert.All(view.Sprites.ToArray(), sprite => Assert.Equal(BitmapFont.Default.Pages[0], sprite.Sprite.Texture));
    }

    private static IEnumerable<int> ExpectedCodepoints() =>
        Enumerable.Range(32, 95).Concat(Enumerable.Range(160, 96));
}

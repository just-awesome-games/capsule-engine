using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// Text is on the one ordered sprite list: it expands where the layout put it, culls per glyph, and
// counts per glyph.
public sealed class TextRenderingTests
{
    [Fact]
    public void ARunOfText_ExpandsToOneSpritePerGlyphAtItsLaidOutPosition()
    {
        FrameView view = new();

        view.Add(new TextIntent(
            FontFixtures.Font(),
            "AB\nA",
            new Vector2(1f, 2f),
            new Vector2(10f, 20f),
            new Vector2(2f, 3f),
            ColorRgba.Black));

        Assert.Equal(3, view.Sprites.Length);

        SpriteIntent first = view.Sprites[0];
        Assert.Equal(new Sprite(FontFixtures.Page, FontFixtures.A.Region), first.Sprite);
        Assert.Equal(new Vector2(12f, 26f), first.Position);
        Assert.Equal(new Vector2(3f, 8f), first.PreviousPosition);
        Assert.Equal(new Vector2(8f, 18f), first.Size);

        // The kerned pen, scaled: 'B' starts three font pixels along.
        Assert.Equal(new Vector2(16f, 26f), view.Sprites[1].Position);

        // A second line sits one scaled line height down, at the pen origin again.
        Assert.Equal(new Vector2(12f, 26f + (FontFixtures.LineHeight * 3f)), view.Sprites[2].Position);

        Assert.All(view.Sprites.ToArray(), sprite => Assert.Equal(ColorRgba.Black, sprite.Color));
    }

    // A run off the layer is rejected whole, and one crossing its edge still culls glyph by glyph.
    [Fact]
    public void EveryGlyph_IsCulledAndCountedOnItsOwn_AndARunOffTheLayerAddsNothing()
    {
        FrameView view = new()
        {
            Camera = new CameraView(new Vector2(0.5f, 5f), new Vector2(3f, 6f)),
        };

        view.Add(new TextIntent(FontFixtures.Font(), "AB", new Vector2(40f, 0f), new Vector2(40f, 0f), Vector2.One, ColorRgba.White));
        Assert.Equal(new RenderMetrics(Submitted: 0, Visible: 0), view.Metrics);

        view.Add(new TextIntent(FontFixtures.Font(), "AB", Vector2.Zero, Vector2.Zero, Vector2.One, ColorRgba.White));

        Assert.Equal(new RenderMetrics(Submitted: 2, Visible: 1), view.Metrics);
        Assert.Equal(FontFixtures.A.Region, Assert.Single(view.Sprites.ToArray()).Sprite.Region);
    }

    // Text overflows a sized box, so a sized axis never rejects the run. Each row's box sits wholly off
    // the layer on its sized axis while the run's last glyph overflows onto it.
    [Theory]
    [InlineData("AB", -7f, 0f, 1f, 0f)]
    [InlineData("A\nA", 0f, -10f, 0f, 1f)]
    public void ASizedRunOverflowingOntoTheLayer_StillDrawsItsVisibleGlyphs(string text, float x, float y, float width, float height)
    {
        FrameView view = new()
        {
            Camera = new CameraView(new Vector2(0.5f, 5f), new Vector2(3f, 6f)),
        };

        Vector2 at = new(x, y);
        view.Add(new TextIntent(FontFixtures.Font(), text, at, at, Vector2.One, ColorRgba.White) { Size = new Vector2(width, height) });

        Assert.Equal(new RenderMetrics(Submitted: 2, Visible: 1), view.Metrics);
        Assert.Single(view.Sprites.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TextThatSaysNothing_SubmitsNothing(string? text)
    {
        FrameView view = new();

        view.Add(new TextIntent(FontFixtures.Font(), text, Vector2.Zero, Vector2.Zero, Vector2.One, ColorRgba.White));
        view.Add(new TextIntent(null, "AB", Vector2.Zero, Vector2.Zero, Vector2.One, ColorRgba.White));

        Assert.Equal(new RenderMetrics(Submitted: 0, Visible: 0), view.Metrics);
    }
}

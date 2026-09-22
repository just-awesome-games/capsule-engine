using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class ColorRgbaTests
{
    [Fact]
    public void TheEndsOfABlend_AreTheColoursThemselves()
    {
        ColorRgba from = new(10, 20, 30, 40);
        ColorRgba to = new(200, 100, 50, 255);

        Assert.Equal(from, ColorRgba.Lerp(from, to, 0f));
        Assert.Equal(to, ColorRgba.Lerp(from, to, 1f));

        // Clamped, so a tween overshooting its curve never blends past either colour.
        Assert.Equal(from, ColorRgba.Lerp(from, to, -0.5f));
        Assert.Equal(to, ColorRgba.Lerp(from, to, 1.5f));
    }

    // Halfway between two channels a byte apart is a half, and rounding it down would make the first
    // half of a fade a tick darker than the second.
    [Fact]
    public void AChannelHalfwayBetweenTwoBytes_RoundsUp()
    {
        ColorRgba blended = ColorRgba.Lerp(new ColorRgba(0, 254, 100, 0), new ColorRgba(255, 0, 101, 3), 0.5f);

        Assert.Equal(new ColorRgba(128, 127, 101, 2), blended);
    }

    [Fact]
    public void AlphaIsBlendedAsAnyOtherChannel_BecauseTheColourIsNotPremultiplied()
    {
        Assert.Equal(
            new ColorRgba(255, 255, 255, 64),
            ColorRgba.Lerp(new ColorRgba(255, 255, 255, 0), ColorRgba.White, 0.25f));
    }

    [Theory]
    [InlineData("#484c68", 72, 76, 104, 255)]
    [InlineData("#484c6880", 72, 76, 104, 128)]
    [InlineData("#484C68FF", 72, 76, 104, 255)]
    public void FromHex_ReadsBothFormsWithTheAlphaLast(string hex, byte r, byte g, byte b, byte a)
    {
        Assert.Equal(new ColorRgba(r, g, b, a), ColorRgba.FromHex(hex));
    }

    [Theory]
    [InlineData("#fff")]
    [InlineData("484c68")]
    [InlineData("#484c68f")]
    [InlineData("#484c68ff0")]
    [InlineData("#484c6g")]
    [InlineData(" #484c68")]
    [InlineData("")]
    public void FromHex_RefusesAnyOtherSpellingAndNamesBothForms(string hex)
    {
        FormatException error = Assert.Throws<FormatException>(() => ColorRgba.FromHex(hex));

        Assert.Contains($"\"{hex}\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("\"#rrggbb\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("\"#rrggbbaa\"", error.Message, StringComparison.Ordinal);
    }
}

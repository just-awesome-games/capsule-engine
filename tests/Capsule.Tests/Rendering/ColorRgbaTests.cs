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
}

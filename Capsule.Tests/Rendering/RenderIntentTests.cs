using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class RenderIntentTests
{
    [Fact]
    public void AColor_CarriesItsChannels()
    {
        ColorRgba color = new(1, 2, 3, 4);

        Assert.Equal(1, color.R);
        Assert.Equal(2, color.G);
        Assert.Equal(3, color.B);
        Assert.Equal(4, color.A);
    }

    [Fact]
    public void TheThreeChannelConstructor_IsOpaque()
    {
        Assert.Equal(new ColorRgba(1, 2, 3, 255), new ColorRgba(1, 2, 3));
    }

    [Fact]
    public void NamedColors_AreTheirCanonicalValues()
    {
        Assert.Equal(new ColorRgba(0, 0, 0, 255), ColorRgba.Black);
        Assert.Equal(new ColorRgba(255, 255, 255, 255), ColorRgba.White);
    }

    [Fact]
    public void Colors_CompareByChannel()
    {
        Assert.NotEqual(ColorRgba.White, ColorRgba.Black);
        Assert.Equal(ColorRgba.White.GetHashCode(), new ColorRgba(255, 255, 255).GetHashCode());
    }

    [Fact]
    public void TheEmptyView_IsShared()
    {
        Assert.Same(FrameView.Empty, FrameView.Empty);
    }
}

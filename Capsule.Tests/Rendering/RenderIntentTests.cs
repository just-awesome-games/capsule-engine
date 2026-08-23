using Capsule.Rendering;
using Capsule.Text;

namespace Capsule.Tests.Rendering;

public sealed class RenderIntentTests
{
    private static readonly PixelTextLayout Layout = PixelText.Layout("HELLO");

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
        Assert.Equal(new ColorRgba(100, 149, 237, 255), ColorRgba.CornflowerBlue);
    }

    [Fact]
    public void Colors_CompareByChannel()
    {
        Assert.NotEqual(ColorRgba.White, ColorRgba.Black);
        Assert.Equal(ColorRgba.White.GetHashCode(), new ColorRgba(255, 255, 255).GetHashCode());
    }

    [Fact]
    public void ATextBlock_CarriesItsPresentation()
    {
        TextBlock block = new(Layout, 8, Anchor.Center, ColorRgba.White);

        Assert.Same(Layout, block.Layout);
        Assert.Equal(8, block.CellPixels);
        Assert.Equal(Anchor.Center, block.Anchor);
        Assert.Equal(ColorRgba.White, block.Color);
        Assert.Equal(block, new TextBlock(Layout, 8, Anchor.Center, ColorRgba.White));
    }

    [Fact]
    public void ATextBlock_RequiresALayout()
    {
        Assert.Throws<ArgumentNullException>(() => new TextBlock(null!, 8, Anchor.Center, ColorRgba.White));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ATextBlock_RequiresAPositiveCellSize(int cellPixels)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextBlock(Layout, cellPixels, Anchor.Center, ColorRgba.White));
    }

    [Fact]
    public void AnEmptyView_HasNothingToDraw()
    {
        Assert.True(FrameView.Empty.TextBlocks.IsEmpty);
        Assert.True(new FrameView().TextBlocks.IsEmpty);
    }

    [Fact]
    public void AView_HoldsItsBlocksInOrder()
    {
        TextBlock first = new(Layout, 8, Anchor.Center, ColorRgba.White);
        TextBlock second = new(Layout, 4, Anchor.Center, ColorRgba.CornflowerBlue);

        FrameView view = new(first, second);

        Assert.Equal([first, second], view.TextBlocks.ToArray());
    }

    [Fact]
    public void AView_IsStableAcrossReads()
    {
        FrameView view = new(new TextBlock(Layout, 8, Anchor.Center, ColorRgba.White));

        Assert.Equal(view.TextBlocks.ToArray(), view.TextBlocks.ToArray());
    }
}

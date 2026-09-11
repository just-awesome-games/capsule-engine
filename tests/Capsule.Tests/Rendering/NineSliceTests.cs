using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// A nine-sliced panel expands to one sprite per slice by region arithmetic: corners at their own texel
// size, edges stretched along one axis, the middle along both.
public sealed class NineSliceTests
{
    // A 12x12 frame cut 3 texels in on every edge, drawn over 40x30.
    private static readonly Sprite Frame = new(new TextureHandle("panel", ".png"), new TextureRegion(10, 20, 12, 12));

    [Fact]
    public void APanel_ExpandsToNineSlices()
    {
        Assert.Equal(9, Expand(new Vector2(40f, 30f)).Length);
    }

    [Fact]
    public void EveryCorner_KeepsItsOwnTexelsAtItsOwnCornerOfThePanel()
    {
        ReadOnlySpan<SpriteIntent> slices = Expand(new Vector2(40f, 30f));

        // Source texels, then where the slice lands and how large it is drawn.
        Assert.Equal(new TextureRegion(10, 20, 3, 3), slices[0].Sprite.Region);
        Assert.Equal(new Vector2(0f, 0f), slices[0].Position);
        Assert.Equal(new Vector2(3f, 3f), slices[0].Size);

        Assert.Equal(new TextureRegion(19, 20, 3, 3), slices[2].Sprite.Region);
        Assert.Equal(new Vector2(37f, 0f), slices[2].Position);

        Assert.Equal(new TextureRegion(10, 29, 3, 3), slices[6].Sprite.Region);
        Assert.Equal(new Vector2(0f, 27f), slices[6].Position);

        Assert.Equal(new TextureRegion(19, 29, 3, 3), slices[8].Sprite.Region);
        Assert.Equal(new Vector2(37f, 27f), slices[8].Position);
        Assert.Equal(new Vector2(3f, 3f), slices[8].Size);
    }

    [Fact]
    public void EachEdge_StretchesAlongItsOwnAxisOnly()
    {
        ReadOnlySpan<SpriteIntent> slices = Expand(new Vector2(40f, 30f));

        // The top edge: the frame's middle column over the panel's middle width, three texels tall.
        Assert.Equal(new TextureRegion(13, 20, 6, 3), slices[1].Sprite.Region);
        Assert.Equal(new Vector2(3f, 0f), slices[1].Position);
        Assert.Equal(new Vector2(34f, 3f), slices[1].Size);

        // The left edge: three texels wide over the panel's middle height.
        Assert.Equal(new TextureRegion(10, 23, 3, 6), slices[3].Sprite.Region);
        Assert.Equal(new Vector2(3f, 24f), slices[3].Size);
    }

    [Fact]
    public void TheMiddle_StretchesOnBothAxes()
    {
        SpriteIntent middle = Expand(new Vector2(40f, 30f))[4];

        Assert.Equal(new TextureRegion(13, 23, 6, 6), middle.Sprite.Region);
        Assert.Equal(new Vector2(3f, 3f), middle.Position);
        Assert.Equal(new Vector2(34f, 24f), middle.Size);
    }

    [Fact]
    public void APanelAsSmallAsItsInsets_DropsTheMiddleAndKeepsTheCornersOnTheirEdges()
    {
        ReadOnlySpan<SpriteIntent> slices = Expand(new Vector2(6f, 6f));

        Assert.Equal(4, slices.Length);
        Assert.Equal(new Vector2(0f, 0f), slices[0].Position);
        Assert.Equal(new Vector2(3f, 0f), slices[1].Position);
        Assert.Equal(new Vector2(3f, 3f), slices[3].Position);
        Assert.All(slices.ToArray(), slice => Assert.Equal(new Vector2(3f, 3f), slice.Size));
    }

    [Fact]
    public void InsetsPastTheRegion_AreCutBackToItAndLeaveNoMiddle()
    {
        ReadOnlySpan<SpriteIntent> slices = Expand(new Vector2(40f, 30f), new SliceInsets(99));

        // The near inset takes the whole region, so the far slices have no texels at all.
        Assert.Equal(new TextureRegion(10, 20, 12, 12), Assert.Single(slices.ToArray()).Sprite.Region);
    }

    [Fact]
    public void NoInsetsAtAll_IsOneStretchedSprite()
    {
        SpriteIntent only = Assert.Single(Expand(new Vector2(40f, 30f), default).ToArray());

        Assert.Equal(new TextureRegion(10, 20, 12, 12), only.Sprite.Region);
        Assert.Equal(new Vector2(40f, 30f), only.Size);
    }

    [Fact]
    public void APanelWithNoExtent_DrawsNothingAndSubmitsNothing()
    {
        FrameView view = new();
        view.Add(new NineSliceIntent(Frame, new SliceInsets(3), Vector2.Zero, Vector2.Zero, Vector2.Zero, ColorRgba.White));

        Assert.Equal(new RenderMetrics(Submitted: 0, Visible: 0), view.Metrics);
    }

    [Fact]
    public void EverySlice_InterpolatesAndTintsWithThePanel()
    {
        FrameView view = new();
        view.Add(new NineSliceIntent(
            Frame,
            new SliceInsets(3),
            new Vector2(-10f, -10f),
            Vector2.Zero,
            new Vector2(40f, 30f),
            ColorRgba.Black));

        foreach (SpriteIntent slice in view.Sprites.ToArray())
        {
            Assert.Equal(slice.Position - new Vector2(10f, 10f), slice.PreviousPosition);
            Assert.Equal(ColorRgba.Black, slice.Color);
        }
    }

    [Fact]
    public void APanel_OccupiesTheRectItCovers()
    {
        NineSliceIntent panel = new(
            Frame,
            new SliceInsets(3),
            Vector2.Zero,
            new Vector2(5f, 7f),
            new Vector2(40f, 30f),
            ColorRgba.White);

        Assert.Equal(new ViewBounds(5f, 7f, 45f, 37f), panel.Bounds);
    }

    [Fact]
    public void APanelSmallerThanItsInsets_OccupiesTheCornersThatOverhangIt()
    {
        // Three-texel corners over two units: the far pair is placed from the far edge, one unit
        // outside the panel on each axis.
        NineSliceIntent panel = Panel(new Vector2(2f, 2f), new SliceInsets(3));

        Assert.Equal(new ViewBounds(-1f, -1f, 3f, 3f), panel.Bounds);
    }

    [Fact]
    public void APanelThatDrawsNothing_OccupiesNoRect()
    {
        Sprite empty = new(Frame.Texture, default);

        Assert.True((Panel(new Vector2(40f, 30f), new SliceInsets(3)) with { Sprite = empty }).Bounds.IsEmpty);
        Assert.True(Panel(Vector2.Zero, new SliceInsets(3)).Bounds.IsEmpty);
    }

    private static NineSliceIntent Panel(Vector2 size, SliceInsets insets) =>
        new(Frame, insets, Vector2.Zero, Vector2.Zero, size, ColorRgba.White);

    private static ReadOnlySpan<SpriteIntent> Expand(Vector2 size) => Expand(size, new SliceInsets(3));

    private static ReadOnlySpan<SpriteIntent> Expand(Vector2 size, SliceInsets insets)
    {
        FrameView view = new();
        view.Add(new NineSliceIntent(Frame, insets, Vector2.Zero, Vector2.Zero, size, ColorRgba.White));

        return view.Sprites;
    }
}

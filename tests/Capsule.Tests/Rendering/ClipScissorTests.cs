using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class ClipScissorTests
{
    // The edges are placed by the canvas's placement and rounded one by one, then the surface cuts them.
    // A rect with no whole pixel left, off the surface or with no area maps to nothing.
    [Theory]
    [InlineData(1.1f, 2.3f, 5.2f, 4.4f, 20f, 10f, 3f, true, 23, 17, 13, 6)]
    [InlineData(-5f, -5f, 5f, 5f, 0f, 0f, 1f, true, 0, 0, 5, 5)]
    [InlineData(95f, 95f, 110f, 110f, 0f, 0f, 1f, true, 95, 95, 5, 5)]
    [InlineData(200f, 0f, 210f, 10f, 0f, 0f, 1f, false, 0, 0, 0, 0)]
    [InlineData(1.1f, 0f, 1.2f, 10f, 0f, 0f, 1f, false, 0, 0, 0, 0)]
    [InlineData(5f, 5f, 5f, 9f, 0f, 0f, 1f, false, 0, 0, 0, 0)]
    public void TheRegion_IsPlacedThenHasEachEdgeRoundedToAWholeSurfacePixel(
        float left,
        float top,
        float right,
        float bottom,
        float originX,
        float originY,
        float scale,
        bool shown,
        int x,
        int y,
        int width,
        int height)
    {
        ScreenPlacement placement = new(new Vector2(originX, originY), scale);

        bool mapped = ClipScissor.TryMap(Rect.FromEdges(left, top, right, bottom), in placement, 100, 100, out int mappedX, out int mappedY, out int mappedWidth, out int mappedHeight);

        Assert.Equal(shown, mapped);
        Assert.Equal((x, y, width, height), (mappedX, mappedY, mappedWidth, mappedHeight));
    }

    // Each level is moved through the step first and the results are intersected. An outer level that holds
    // still over an inner one sliding across it shows the outer whole halfway, where intersecting the ends
    // first would show only the middle of it.
    [Fact]
    public void AClip_InterpolatesEachLevelBeforeIntersectingThem()
    {
        Rect outer = Rect.FromEdges(0f, 0f, 10f, 10f);
        ClipRegion[] levels = [new(Rect.FromEdges(-5f, 0f, 5f, 10f), Rect.FromEdges(5f, 0f, 15f, 10f)), new(outer, outer)];

        Assert.Equal(outer, ClipScissor.Shown(levels, 0.5f));
        Assert.Equal(Rect.FromEdges(0f, 0f, 5f, 10f), ClipScissor.Shown(levels, 0f));
    }

    // Two clips that abut on the canvas share one pixel boundary on the surface, whatever the scale.
    [Fact]
    public void TwoAbuttingRegions_ShareOneBoundaryOnTheSurface()
    {
        ScreenPlacement placement = new(Vector2.Zero, 3f);

        ClipScissor.TryMap(Rect.FromEdges(0f, 0f, 10.4f, 5f), in placement, 100, 100, out int leftX, out _, out int leftWidth, out _);
        ClipScissor.TryMap(Rect.FromEdges(10.4f, 0f, 20f, 5f), in placement, 100, 100, out int rightX, out _, out _, out _);

        Assert.Equal(rightX, leftX + leftWidth);
    }
}

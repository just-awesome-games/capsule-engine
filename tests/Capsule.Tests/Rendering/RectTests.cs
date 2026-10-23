using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// The two ways to build a rect, and containment on the half-open region the pointer is tested against.
// A point on a shared edge lands in exactly one of two abutting rects.
public sealed class RectTests
{
    private static readonly Rect Box = Rect.FromEdges(10f, 20f, 30f, 40f);

    [Fact]
    public void EdgesAndACornerWithAnExtent_BuildTheSameRect()
    {
        Assert.Equal(Box, new Rect(10f, 20f, 20f, 20f));
    }

    [Fact]
    public void Center_LiesMidwayBetweenTheEdges()
    {
        Assert.Equal(new Vector2(20f, 30f), Box.Center);
    }

    // Inside, then the low edges and the corner they meet at, then the high edges and their corner,
    // then outside on each axis.
    [Theory]
    [InlineData(20f, 30f, true)]
    [InlineData(10.5f, 39.5f, true)]
    [InlineData(10f, 30f, true)]
    [InlineData(20f, 20f, true)]
    [InlineData(10f, 20f, true)]
    [InlineData(30f, 30f, false)]
    [InlineData(20f, 40f, false)]
    [InlineData(30f, 40f, false)]
    [InlineData(9f, 30f, false)]
    [InlineData(20f, 41f, false)]
    public void APoint_IsContainedOnTheHalfOpenRegion(float x, float y, bool contained)
    {
        Assert.Equal(contained, Box.Contains(new Vector2(x, y)));
    }

    [Fact]
    public void AnEmptyRect_ContainsNothing()
    {
        Assert.False(Rect.FromEdges(10f, 20f, 10f, 40f).Contains(new Vector2(10f, 30f)));
        Assert.False(default(Rect).Contains(Vector2.Zero));
    }

    [Fact]
    public void ANonFinitePointOrEdge_IsNeverContained()
    {
        Assert.False(Box.Contains(new Vector2(float.NaN, 30f)));
        Assert.False(Rect.FromEdges(float.NaN, 20f, 30f, 40f).Contains(new Vector2(20f, 30f)));

        // An infinite edge is no region either. Containment and emptiness agree on it.
        Rect unbounded = Rect.FromEdges(0f, 0f, float.PositiveInfinity, 10f);

        Assert.True(unbounded.IsEmpty);
        Assert.False(unbounded.Contains(new Vector2(5f, 5f)));
    }
}

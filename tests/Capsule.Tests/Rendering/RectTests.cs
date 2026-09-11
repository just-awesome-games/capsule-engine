using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// Containment on the open region: what the pointer is tested against, so a point on a shared edge must
// land in one rect at most — in neither, here.
public sealed class RectTests
{
    private static readonly Rect Box = new(10f, 20f, 30f, 40f);

    [Fact]
    public void ACornerAndAnExtent_AreTheSameRectAsFourEdges()
    {
        Assert.Equal(Box, new Rect(new Vector2(10f, 20f), new Vector2(20f, 20f)));
        Assert.Equal(new Vector2(10f, 20f), Box.Position);
        Assert.Equal(new Vector2(20f, 20f), Box.Size);
    }

    [Fact]
    public void ARectWhoseEdgesAreCrossed_ReportsANegativeExtent()
    {
        Assert.Equal(new Vector2(-4f, 20f), new Rect(10f, 20f, 6f, 40f).Size);
    }

    [Fact]
    public void APointInside_IsContained()
    {
        Assert.True(Box.Contains(new Vector2(20f, 30f)));
        Assert.True(Box.Contains(new Vector2(10.5f, 39.5f)));
    }

    [Theory]
    [InlineData(10f, 30f)]
    [InlineData(30f, 30f)]
    [InlineData(20f, 20f)]
    [InlineData(20f, 40f)]
    public void APointOnAnEdge_IsOutside(float x, float y)
    {
        Assert.False(Box.Contains(new Vector2(x, y)));
    }

    [Fact]
    public void APointOutside_IsNotContained()
    {
        Assert.False(Box.Contains(new Vector2(9f, 30f)));
        Assert.False(Box.Contains(new Vector2(20f, 41f)));
    }

    [Fact]
    public void AnEmptyRect_ContainsNothing()
    {
        Assert.False(new Rect(10f, 20f, 10f, 40f).Contains(new Vector2(10f, 30f)));
        Assert.False(default(Rect).Contains(Vector2.Zero));
    }

    [Fact]
    public void ANonFinitePointOrEdge_IsNeverContained()
    {
        Assert.False(Box.Contains(new Vector2(float.NaN, 30f)));
        Assert.False(new Rect(float.NaN, 20f, 30f, 40f).Contains(new Vector2(20f, 30f)));

        // An infinite edge is no region either, so containment and emptiness agree on it.
        Rect unbounded = new(0f, 0f, float.PositiveInfinity, 10f);

        Assert.True(unbounded.IsEmpty);
        Assert.False(unbounded.Contains(new Vector2(5f, 5f)));
    }
}

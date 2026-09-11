using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// Containment on the open region: what the pointer is tested against, so a point on a shared edge must
// land in one rect at most — in neither, here.
public sealed class ViewBoundsTests
{
    private static readonly ViewBounds Rect = new(10f, 20f, 30f, 40f);

    [Fact]
    public void APointInside_IsContained()
    {
        Assert.True(Rect.Contains(new Vector2(20f, 30f)));
        Assert.True(Rect.Contains(new Vector2(10.5f, 39.5f)));
    }

    [Theory]
    [InlineData(10f, 30f)]
    [InlineData(30f, 30f)]
    [InlineData(20f, 20f)]
    [InlineData(20f, 40f)]
    public void APointOnAnEdge_IsOutside(float x, float y)
    {
        Assert.False(Rect.Contains(new Vector2(x, y)));
    }

    [Fact]
    public void APointOutside_IsNotContained()
    {
        Assert.False(Rect.Contains(new Vector2(9f, 30f)));
        Assert.False(Rect.Contains(new Vector2(20f, 41f)));
    }

    [Fact]
    public void AnEmptyRect_ContainsNothing()
    {
        Assert.False(new ViewBounds(10f, 20f, 10f, 40f).Contains(new Vector2(10f, 30f)));
        Assert.False(default(ViewBounds).Contains(Vector2.Zero));
    }

    [Fact]
    public void ANonFinitePointOrEdge_IsNeverContained()
    {
        Assert.False(Rect.Contains(new Vector2(float.NaN, 30f)));
        Assert.False(new ViewBounds(float.NaN, 20f, 30f, 40f).Contains(new Vector2(20f, 30f)));

        // An infinite edge is no region either, so containment and emptiness agree on it.
        ViewBounds unbounded = new(0f, 0f, float.PositiveInfinity, 10f);

        Assert.True(unbounded.IsEmpty);
        Assert.False(unbounded.Contains(new Vector2(5f, 5f)));
    }
}

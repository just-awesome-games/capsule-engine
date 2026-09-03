using System.Numerics;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

public sealed class PixelGridTests
{
    [Fact]
    public void Snap_PutsAHalfPixelPositionOnAWholePixelAtUnitScale()
    {
        Assert.Equal(new Vector2(13f, -8f), PixelGrid.Snap(new Vector2(12.5f, -7.5f), 1f));
    }

    [Theory]
    [InlineData(12.5f, 3.95f)]
    [InlineData(-0.37f, 3.95f)]
    [InlineData(101.111f, 6f)]
    public void Snap_LandsOnAWholeSurfacePixelAtAFractionalScale(float value, float scale)
    {
        float surface = PixelGrid.Snap(new Vector2(value, value), scale).X * scale;

        Assert.Equal(MathF.Round(surface), surface, 0.001);
    }

    [Fact]
    public void Snap_LeavesAPositionAlreadyOnTheGrid()
    {
        Assert.Equal(new Vector2(4f, 9f), PixelGrid.Snap(new Vector2(4f, 9f), 4f));
    }
}

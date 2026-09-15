using System.Numerics;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

public sealed class PixelGridTests
{
    // Whatever the scale, and whether or not the position is already on the grid, a snapped
    // position lands on a whole surface pixel.
    [Theory]
    [InlineData(12.5f, 1f)]
    [InlineData(4f, 4f)]
    [InlineData(12.5f, 3.95f)]
    [InlineData(-0.37f, 3.95f)]
    [InlineData(101.111f, 6f)]
    public void Snap_LandsOnAWholeSurfacePixel(float value, float scale)
    {
        float surface = PixelGrid.Snap(new Vector2(value, value), scale).X * scale;

        Assert.Equal(MathF.Round(surface), surface, 0.001);
    }
}

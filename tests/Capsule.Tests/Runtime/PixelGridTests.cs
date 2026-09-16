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

    // A midpoint rounds down whichever side of zero it is on and however the origin's own float
    // error nudges it, so a point a whole number of pixels from another snaps a whole number of
    // pixels from it and a followed point on a half pixel holds its pixel.
    [Theory]
    [InlineData(10.5f, 1f)]
    [InlineData(-10.5f, 1f)]
    [InlineData(45.25f, 2f)]
    public void AMidpoint_RoundsDownOnBothSidesOfZero(float value, float scale)
    {
        Vector2 snapped = PixelGrid.Snap(new Vector2(value, value), scale);

        Assert.Equal(MathF.Floor(value * scale) / scale, snapped.X);
        Assert.Equal(snapped.X, snapped.Y);
    }

    [Fact]
    public void PointsWholePixelsApart_SnapWholePixelsApartFromAnyOrigin()
    {
        Vector2 origin = new(18.5000019f, 981.4999981f);

        Vector2 near = PixelGrid.SnapOffset(origin, new Vector2(16f, 16f), 3f);
        Vector2 far = PixelGrid.SnapOffset(origin, new Vector2(1000f, 1000f), 3f);

        Assert.Equal(new Vector2(984f, 984f), far - near);
    }
}

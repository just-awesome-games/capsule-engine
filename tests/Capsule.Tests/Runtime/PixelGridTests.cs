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
        float surface = PixelGrid.Pixels(Vector2.Zero, new Vector2(value, value), scale).X;

        Assert.Equal(MathF.Round(surface), surface, 0.001);
    }

    // A midpoint rounds down whichever side of zero it is on and however the origin's own float
    // error nudges it. A point a whole number of pixels from another snaps a whole number of
    // pixels from it and a followed point on a half pixel holds its pixel.
    [Theory]
    [InlineData(10.5f, 1f)]
    [InlineData(-10.5f, 1f)]
    [InlineData(45.25f, 2f)]
    public void AMidpoint_RoundsDownOnBothSidesOfZero(float value, float scale)
    {
        Vector2 snapped = PixelGrid.Pixels(Vector2.Zero, new Vector2(value, value), scale) / scale;

        Assert.Equal(MathF.Floor(value * scale) / scale, snapped.X);
        Assert.Equal(snapped.X, snapped.Y);
    }

    [Fact]
    public void PointsWholePixelsApart_SnapWholePixelsApartFromAnyOrigin()
    {
        Vector2 origin = new(18.5000019f, 981.4999981f);

        Vector2 near = PixelGrid.Pixels(origin, new Vector2(16f, 16f), 3f);
        Vector2 far = PixelGrid.Pixels(origin, new Vector2(1000f, 1000f), 3f);

        Assert.Equal(new Vector2(984f * 3f, 984f * 3f), far - near);
    }

    // The corner sweeps a whole pixel. Each pair holds a point a fraction of a pixel off the grid, and the
    // last sits far out, where a float holds a position least finely.
    [Theory]
    [InlineData(16f, 16.5f, 1f)]
    [InlineData(16.3f, 40f, 1f)]
    [InlineData(7000.25f, 7100.75f, 3f)]
    public void WorldPoints_KeepTheirPixelDistanceWhereverTheCornerSits(float near, float far, float scale)
    {
        float? apart = null;

        for (int step = 0; step < 97; step++)
        {
            Vector2 corner = new(-3f + (step / 97f));
            (Vector2 frame, Vector2 anchor) = PixelGrid.Corners(corner, scale);
            float distance = PixelGrid.Pixels(anchor, new Vector2(far), scale).X - PixelGrid.Pixels(anchor, new Vector2(near), scale).X;

            Assert.Equal(MathF.Round(frame.X * scale), frame.X * scale, 0.001);
            Assert.InRange(MathF.Abs(frame.X - corner.X) * scale, 0f, 0.54f);
            Assert.Equal(apart ??= distance, distance);
        }
    }

    // The followed point is a few float steps off its whole distance from the corner, as a view lerped apart
    // from its subject is, and the sweep puts the corner exactly on the edge between two pixels.
    [Theory]
    [InlineData(100f, 1f)]
    [InlineData(4090f, 1f)]
    [InlineData(8100f, 2f)]
    public void AFollowedPoint_HoldsItsPixelAsTheCornerCrossesPixels(float from, float scale)
    {
        const float Apart = 128f;

        for (int step = 0; step < 4000; step++)
        {
            float corner = from + (step * 0.00390625f) + (0.53125f / scale);
            for (int noise = -2; noise <= 2; noise++)
            {
                float followed = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(corner + Apart) + noise);
                (_, Vector2 anchor) = PixelGrid.Corners(new Vector2(corner), scale);

                Assert.Equal(Apart * scale, PixelGrid.Pixels(anchor, new Vector2(followed), scale).X);
            }
        }
    }
}

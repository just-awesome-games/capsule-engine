using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

// A shape's horizontal extent across a band, which a label's row reads. Every row is one the shape's bounds
// alone would widen.
public sealed class Shape2DExtentTests
{
    // The capsule runs from (0, 0) to (10, 10) with radius sqrt 2, which takes in |x - y| <= 2 along its middle.
    // From y 4 to 6 that spans 2 to 8. From y -1 to 0 the end disk reaches -sqrt 2 at y 0, and the strip's
    // corner at (2, 0) is the right edge.
    // The triangle (0, 0), (10, 0), (5, 10) is 10 - y wide, centred on 5. From y 8 to 12 it spans 4 to 6 at
    // y 8, and from y 4 to 6 it spans 2 to 8 at y 4.
    // The square (0, 0) to (10, 10) rounded by 2 spans -sqrt 3 to 10 + sqrt 3 at y -1, through the corner disks
    // and the strip under its bottom edge. Its bounds span -2 to 12.
    [Theory]
    [InlineData("capsule", 4f, 6f, 2f, 8f)]
    [InlineData("capsule", -1f, 0f, -1.41421f, 2f)]
    [InlineData("triangle", 8f, 12f, 4f, 6f)]
    [InlineData("triangle", 4f, 6f, 2f, 8f)]
    [InlineData("rounded", -2f, -1f, -1.73205f, 11.73205f)]
    public void Extent_IsTheShapesExactSpanAcrossTheBand(string shape, float top, float bottom, float left, float right)
    {
        Shape2D built = shape switch
        {
            "capsule" => Shape2D.Capsule(Vector2.Zero, new Vector2(10f, 10f), MathF.Sqrt(2f)),
            "triangle" => Shape2D.Polygon([Vector2.Zero, new Vector2(10f, 0f), new Vector2(5f, 10f)]),
            _ => Shape2D.Polygon([Vector2.Zero, new Vector2(10f, 0f), new Vector2(10f, 10f), new Vector2(0f, 10f)], 2f),
        };

        built.Extent(top, bottom, out float actualLeft, out float actualRight);

        Assert.Equal(left, actualLeft, 1e-4f);
        Assert.Equal(right, actualRight, 1e-4f);
    }
}

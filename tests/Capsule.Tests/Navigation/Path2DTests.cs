using System.Numerics;
using Capsule.Navigation;
using Capsule.Rendering;

namespace Capsule.Tests.Navigation;

public sealed class Path2DTests
{
    public static TheoryData<Vector2[], string> Refused => new()
    {
        { [Vector2.Zero], "A path runs through at least two points, and this one has 1. Add a point." },
        { [Vector2.Zero, new(float.NaN, 0f)], "Point 1 of the path is <NaN, 0>. Write every coordinate as a finite number." },
        { [Vector2.One, Vector2.One, Vector2.One], "Every point of the path is the same, and a path needs length. Move a point apart from the others." },
        { [Vector2.Zero, new(2e19f, 0f)], "The path is too long to measure. Bring its points closer together." },
    };

    // A repeated point is a segment of no length, which a distance passes over without dividing by it.
    [Fact]
    public void APath_MeasuresItsPoints_AndClampsADistanceToItsEnds()
    {
        Vector2[] points = [Vector2.Zero, new(30f, 40f), new(30f, 40f), new(30f, 0f)];
        Path2D path = new(points);
        points[0] = Vector2.One;

        Assert.Equal([Vector2.Zero, new(30f, 40f), new(30f, 40f), new(30f, 0f)], path.Points.ToArray());
        Assert.Equal([0f, 50f, 50f, 90f], path.Distances.ToArray());
        Assert.Equal(90f, path.Length);
        Assert.Equal(Rect.FromEdges(0f, 0f, 30f, 40f), path.Bounds);
        Assert.False(path.Closed);
        Assert.Equal(new Vector2(15f, 20f), path.PointAt(25f));
        Assert.Equal(new Vector2(30f, 40f), path.PointAt(50f));
        Assert.Equal(new Vector2(30f, 20f), path.PointAt(70f));
        Assert.Equal(Vector2.Zero, path.PointAt(-5f));
        Assert.Equal(new Vector2(30f, 0f), path.PointAt(200f));
    }

    [Fact]
    public void APathEndingOnItsFirstPoint_IsClosed()
    {
        Path2D loop = new(Vector2.Zero, new Vector2(10f, 0f), new Vector2(10f, 10f), Vector2.Zero);

        Assert.True(loop.Closed);
        Assert.Equal(Vector2.Zero, loop.PointAt(loop.Length));
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void PointsThatMakeNoPath_AreRefusedNamingTheFix(Vector2[] points, string fix)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => new Path2D(points));

        Assert.StartsWith(fix, refused.Message, StringComparison.Ordinal);
    }
}

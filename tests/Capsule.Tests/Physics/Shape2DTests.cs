using System.Numerics;
using Capsule.Physics;
using Capsule.Physics.Internal;

namespace Capsule.Tests.Physics;

public sealed class Shape2DTests
{
    [Fact]
    public void AabbCenter_StaysFiniteForSameSignExtremeCorners()
    {
        Aabb2D box = new(new Vector2(2e38f, -3e38f), new Vector2(3e38f, -2e38f));

        Assert.Equal(new Vector2(2.5e38f, -2.5e38f), box.Center);
    }

    // Each input is a real float while the box or edge it describes is not. The tree's area
    // heuristic and the mover's inset compute with that extent, and an infinity there spreads.
    [Fact]
    public void AShapeWhoseBoundsOrEdgesOverflow_IsRefusedThoughEveryInputIsFinite()
    {
        Assert.Throws<ArgumentException>(() => Shape2D.Box(new Aabb2D(new Vector2(-3e38f, -1f), new Vector2(3e38f, 1f))));
        Assert.Throws<ArgumentException>(() => Shape2D.Box(Vector2.Zero, new Vector2(3e38f, 3e38f)));
        Assert.Throws<ArgumentException>(() => Shape2D.Circle(Vector2.Zero, 2e38f));
        Assert.Throws<ArgumentException>(() => Shape2D.Circle(new Vector2(3e38f, 0f), 3e38f));
        Assert.Throws<ArgumentException>(() => Shape2D.Capsule(new Vector2(-3e38f, 0f), new Vector2(3e38f, 0f), 1f));
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon(
            [new Vector2(-3e38f, -1f), new Vector2(3e38f, -1f), new Vector2(0f, 1f)]));

        // A shape that fits where it was built and not where it is being put.
        Assert.Throws<ArgumentException>(() => Shape2D.Circle(Vector2.Zero, 8e37f).Translated(new Vector2(3e38f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)).Translated(new Vector2(float.NaN, 0f)));
    }

    // As far out as a unit box can go and still have width, it is placed and found.
    [Fact]
    public void AShapeAtTheFurthestCoordinateItKeepsItsExtent_IsPlacedAndFound()
    {
        Shape2D unit = Shape2D.Box(Vector2.Zero, new Vector2(1f, 1f));

        // Float spacing reaches one unit at 2^23. One step below it, the box still has width.
        const float Furthest = 4194304f;
        Shape2D placed = unit.Translated(new Vector2(Furthest, 0f));

        Assert.True(placed.Bounds.Size.X > 0f);
        Assert.True(placed.Bounds.Size.Y > 0f);

        CollisionWorld2D world = new();
        world.Add(unit, new Vector2(Furthest, 0f), world.Layer("wall"));

        Assert.True(world.Raycast(
            new Vector2(Furthest - 10f, 0.5f),
            Vector2.UnitX,
            100f,
            CollisionFilter.Everything,
            out RayHit2D hit));

        Assert.False(hit.Target.IsGridCell);
        Assert.True(float.IsFinite(hit.Distance));
    }

    // The largest extent whose derived geometry stays finite is a shape, and answers with numbers.
    [Fact]
    public void AShapeAtTheLargestExtentThatStaysFinite_IsBuiltAndAnsweredFinitely()
    {
        Shape2D wide = Shape2D.Box(Vector2.Zero, new Vector2(1e38f, 1e38f));

        Assert.True(float.IsFinite(wide.Bounds.Size.X));
        Assert.True(float.IsFinite(wide.Bounds.Perimeter));

        CollisionWorld2D world = new();
        world.Add(wide, Vector2.Zero, world.Layer("wall"));

        Assert.True(world.Raycast(new Vector2(-10f, 5f), Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D hit));
        Assert.Equal(10f, hit.Distance, 3);
        Assert.True(float.IsFinite(hit.Normal.X) && float.IsFinite(hit.Normal.Y));
    }

    [Fact]
    public void EveryFactory_RefusesAShapeWithNoWellDefinedOutline()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Shape2D.Circle(Vector2.Zero, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Shape2D.Circle(Vector2.Zero, float.NaN));
        Assert.Throws<ArgumentException>(() => Shape2D.Capsule(new Vector2(4f, 4f), new Vector2(4f, 4f), 2f));
        Assert.Throws<ArgumentException>(() => Shape2D.Box(new Aabb2D(new Vector2(4f, 0f), new Vector2(0f, 8f))));
        Assert.Throws<ArgumentException>(() => Shape2D.Box(new Aabb2D(Vector2.Zero, new Vector2(8f, 0f))));
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon([Vector2.Zero, Vector2.One]));
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon(
            [new Vector2(0f, 0f), new Vector2(8f, 0f), new Vector2(8f, 8f), new Vector2(4f, 4f), new Vector2(0f, 8f)]));
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon(
            [new Vector2(0f, -10f), new Vector2(6f, 8f), new Vector2(-9f, -3f), new Vector2(9f, -3f), new Vector2(-6f, 8f)]));
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon(
            [new Vector2(0f, 0f), new Vector2(8f, 0f), new Vector2(4f, 4f), new Vector2(8f, 8f)]));
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon([new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(8f, 0f)]));
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon([new Vector2(0f, 0f), new Vector2(0f, 0.001f), new Vector2(8f, 8f)]));
    }

    // Winding decides which way edge normals point. The same outline authored backwards must
    // still face them outward.
    [Fact]
    public void Polygon_AcceptsEitherWindingOrderAndStillFacesItsNormalsOutward()
    {
        Vector2[] forwards = [new(20f, -8f), new(36f, 0f), new(20f, 8f)];
        Vector2[] backwards = [new(20f, 8f), new(36f, 0f), new(20f, -8f)];

        Assert.Equal((20f, new Vector2(-1f, 0f)), FirstFace(forwards));
        Assert.Equal(FirstFace(forwards), FirstFace(backwards));

        static (float Distance, Vector2 Normal) FirstFace(Vector2[] points)
        {
            CollisionWorld2D world = new();
            world.Add(Shape2D.Polygon(points), Vector2.Zero, world.Layer("target"));

            Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D hit));

            return (hit.Distance, hit.Normal);
        }
    }

    // A polygon past four corners shares one stored outline between every copy. Moving or scaling a
    // copy never reaches the outline the others read.
    [Fact]
    public void APolygonPastFourCorners_MovesAndScalesAsItsCorners_AndLeavesTheOriginalAsBuilt()
    {
        Vector2[] corners = [new(0f, 0f), new(8f, -4f), new(16f, 0f), new(16f, 8f), new(8f, 12f), new(0f, 8f)];
        Shape2D hexagon = Shape2D.Polygon(corners);

        Shape2D moved = hexagon.Translated(new Vector2(4f, 2f)).Translated(new Vector2(1f, 1f));
        Shape2D scaled = hexagon.Scaled(new Vector2(2f, 2f));

        Assert.Equal(corners.ToHashSet(), Enumerable.Range(0, 6).Select(hexagon.Point).ToHashSet());
        for (int index = 0; index < 6; index++)
        {
            Assert.Equal(hexagon.Point(index) + new Vector2(5f, 3f), moved.Point(index));
            Assert.Equal(hexagon.Point(index) * 2f, scaled.Point(index));
        }

        Assert.Equal(hexagon.Bounds.Translated(new Vector2(5f, 3f)), moved.Bounds);
    }

    // The store never frees an outline. Building the same polygon again must not grow it.
    [Fact]
    public void HullStore_KeepsEachDistinctOutlineOnce()
    {
        Vector2[] outline = [new(-3f, 1f), new(5f, -7f), new(13f, 1f), new(13f, 9f), new(5f, 17f), new(-3f, 9f)];

        int stored = HullStore2D.Add(outline);

        Assert.Equal(stored, HullStore2D.Add([.. outline]));
        Assert.NotEqual(stored, HullStore2D.Add(outline.AsSpan(1)));
    }

    [Fact]
    public void Bounds_CoverThePointsAndTheRadius()
    {
        Shape2D capsule = Shape2D.Capsule(new Vector2(4f, 4f), new Vector2(4f, 12f), 3f);

        Assert.Equal(new Vector2(1f, 1f), capsule.Bounds.Min);
        Assert.Equal(new Vector2(7f, 15f), capsule.Bounds.Max);
    }

    // The inside and the edge count, as a point query finds them. A point near a zero-radius hull's corner
    // leaves GJK a rounding residue that must still read as inside.
    [Theory]
    [InlineData(0.03f, 0.03f, true)]
    [InlineData(0.5f, 0.5f, true)]
    [InlineData(1f, 0.5f, true)]
    [InlineData(1.01f, 0.5f, false)]
    [InlineData(-0.01f, 0f, false)]
    public void Contains_TakesTheInsideAndTheEdge(float x, float y, bool inside)
    {
        Assert.Equal(inside, Shape2D.Box(Vector2.Zero, Vector2.One).Contains(new Vector2(x, y)));
    }

    // About the shape's own origin. A box offset from it moves with its corners rather than
    // growing in place.
    [Fact]
    public void Scaled_MultipliesEveryPointAboutTheShapesOwnOrigin()
    {
        Shape2D scaled = Shape2D.Box(new Vector2(2f, 4f), new Vector2(8f, 8f)).Scaled(new Vector2(2f, 0.5f));

        Assert.Equal(new Vector2(4f, 2f), scaled.Bounds.Min);
        Assert.Equal(new Vector2(20f, 6f), scaled.Bounds.Max);
        Assert.Equal(ShapeKind2D.Box, scaled.Kind);
    }

    // A radius is one distance. A non-uniform scale would name a shape the narrowphase has no
    // representation for.
    [Fact]
    public void Scaled_ScalesARoundedShapesRadiusWithItsPoints()
    {
        Shape2D circle = Shape2D.Circle(new Vector2(4f, 0f), 2f).Scaled(new Vector2(3f, 3f));
        Shape2D capsule = Shape2D.Capsule(new Vector2(0f, -4f), new Vector2(0f, 4f), 1f).Scaled(new Vector2(2f, 2f));

        Assert.Equal(6f, circle.Radius);
        Assert.Equal(new Vector2(12f, 0f), circle.Point(0));
        Assert.Equal(2f, capsule.Radius);
        Assert.Equal(new Vector2(0f, -8f), capsule.Point(0));
        Assert.Equal(new Vector2(0f, 8f), capsule.Point(1));
    }

    [Fact]
    public void Scaled_RefusesANonUniformScaleOnARoundedShape()
    {
        ArgumentException circle = Assert.Throws<ArgumentException>(
            () => Shape2D.Circle(Vector2.Zero, 2f).Scaled(new Vector2(2f, 1f)));
        ArgumentException capsule = Assert.Throws<ArgumentException>(
            () => Shape2D.Capsule(new Vector2(0f, -4f), new Vector2(0f, 4f), 1f).Scaled(new Vector2(1f, 2f)));
        ArgumentException rounded = Assert.Throws<ArgumentException>(
            () => Shape2D.Polygon([new Vector2(0f, -4f), new Vector2(8f, 0f), new Vector2(0f, 4f)], 1f)
                .Scaled(new Vector2(2f, 1f)));

        Assert.Contains("Circle", circle.Message, StringComparison.Ordinal);
        Assert.Contains("Capsule", capsule.Message, StringComparison.Ordinal);
        Assert.Contains("Polygon", rounded.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, -2f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, float.PositiveInfinity)]
    public void Scaled_RefusesAFactorThatIsNotPositiveAndFinite(float x, float y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)).Scaled(new Vector2(x, y)));
    }

    // Scaled far enough down, a box's corners land on each other and there is no region left.
    [Fact]
    public void Scaled_RefusesAShapeItWouldCollapse()
    {
        Assert.Throws<ArgumentException>(
            () => Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)).Scaled(new Vector2(1e-6f, 1e-6f)));
    }

    [Fact]
    public void Scaled_RefusesTheDefaultShape()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => default(Shape2D).Scaled(Vector2.One));
        Assert.Contains("default Shape2D", refused.Message, StringComparison.Ordinal);
    }
}

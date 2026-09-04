using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

public sealed class Shape2DTests
{
    // Corners within range are not enough: the width between them is what the mover's inset and the
    // tree's area heuristic compute with, and an infinity there is computed with silently.
    [Fact]
    public void AShapeWhoseExtentOverflows_IsRefusedThoughEveryCornerIsFinite()
    {
        Assert.Throws<ArgumentException>(() => Shape2D.Box(new Aabb2D(new Vector2(-3e38f, -1f), new Vector2(3e38f, 1f))));
        Assert.Throws<ArgumentException>(() => Shape2D.Box(Vector2.Zero, new Vector2(3e38f, 3e38f)));
        Assert.Throws<ArgumentException>(() => Shape2D.Circle(Vector2.Zero, 2e38f));
    }

    // A polygon spanning the float range has finite points and infinite edges, whose normals come
    // out NaN rather than refused.
    [Fact]
    public void APolygonWhoseEdgesOverflow_IsRefusedThoughEveryPointIsFinite()
    {
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon(
            [new Vector2(-3e38f, -1f), new Vector2(3e38f, -1f), new Vector2(0f, 1f)]));

        Assert.Throws<ArgumentException>(() => Shape2D.Capsule(new Vector2(-3e38f, 0f), new Vector2(3e38f, 0f), 1f));
    }

    // Far enough out, the floats either side of a small shape are the same float: the hull survives
    // but the broadphase box collapses, and the tree skips geometry the narrowphase still holds.
    [Fact]
    public void AShapeWhoseExtentCollapsesUnderTranslation_IsRefusedWhereItIsPlaced()
    {
        Shape2D unit = Shape2D.Box(Vector2.Zero, new Vector2(1f, 1f));

        Assert.Throws<ArgumentException>(() => unit.Translated(new Vector2(3e38f, 0f)));

        // A capsule loses its two points to the same coordinate before its bounds go.
        Shape2D capsule = Shape2D.Capsule(new Vector2(-10f, 0f), new Vector2(10f, 0f), 1f);
        Assert.Throws<ArgumentException>(() => capsule.Translated(new Vector2(3e38f, 0f)));

        // And every seam that places a shape refuses it for the same reason.
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
        ColliderHandle handle = world.Add(unit, Vector2.Zero, item, CollisionFilter.None);

        Assert.Throws<ArgumentException>(() => world.Add(unit, new Vector2(3e38f, 0f), item, CollisionFilter.None));
        Assert.Throws<ArgumentException>(() => world.SetPosition(handle, new Vector2(3e38f, 0f)));
        Assert.Throws<ArgumentException>(
            () => world.Overlap(unit, new Vector2(3e38f, 0f), CollisionFilter.Everything, default));

        Assert.Equal(Vector2.Zero, world.PositionOf(handle));
    }

    // The other side of that boundary: as far out as a unit box can go and still have width, it is
    // still found.
    [Fact]
    public void AShapeAtTheFurthestCoordinateItKeepsItsExtent_IsPlacedAndFound()
    {
        Shape2D unit = Shape2D.Box(Vector2.Zero, new Vector2(1f, 1f));

        // Float spacing reaches one unit at 2^23; one step below it, the box still has width.
        const float Furthest = 4194304f;
        Shape2D placed = unit.Translated(new Vector2(Furthest, 0f));

        Assert.True(placed.Bounds.Size.X > 0f);
        Assert.True(placed.Bounds.Size.Y > 0f);

        CollisionWorld2D world = new();
        world.Add(unit, new Vector2(Furthest, 0f), world.Layer("wall"), CollisionFilter.None);

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
        world.Add(wide, Vector2.Zero, world.Layer("wall"), CollisionFilter.None);

        Assert.True(world.Raycast(new Vector2(-10f, 5f), Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D hit));
        Assert.Equal(10f, hit.Distance, 3);
        Assert.True(float.IsFinite(hit.Normal.X) && float.IsFinite(hit.Normal.Y));
    }

    [Fact]
    public void Circle_RejectsARadiusThatIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Shape2D.Circle(Vector2.Zero, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Shape2D.Circle(Vector2.Zero, float.NaN));
    }

    [Fact]
    public void Capsule_RejectsCoincidentEndpoints()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Shape2D.Capsule(new Vector2(4f, 4f), new Vector2(4f, 4f), 2f));

        Assert.Contains("capsule of no length is a circle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Box_RejectsAnInvertedOrFlatRectangle()
    {
        Assert.Throws<ArgumentException>(() => Shape2D.Box(new Aabb2D(new Vector2(4f, 0f), new Vector2(0f, 8f))));
        Assert.Throws<ArgumentException>(() => Shape2D.Box(new Aabb2D(Vector2.Zero, new Vector2(8f, 0f))));
    }

    [Fact]
    public void Polygon_RejectsAPointSetOutsideTheUnionsLimits()
    {
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon([Vector2.Zero, Vector2.One]));
        Assert.Throws<ArgumentException>(() => Shape2D.Polygon(
            [
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f), new Vector2(3f, 0f),
                new Vector2(4f, 1f), new Vector2(3f, 2f), new Vector2(2f, 2f), new Vector2(1f, 2f),
                new Vector2(0f, 1f),
            ]));
    }

    [Fact]
    public void Polygon_RejectsAConcaveOrCollinearOutline()
    {
        ArgumentException concave = Assert.Throws<ArgumentException>(() => Shape2D.Polygon(
            [new Vector2(0f, 0f), new Vector2(8f, 0f), new Vector2(4f, 4f), new Vector2(8f, 8f), new Vector2(0f, 8f)]));
        Assert.Contains("collinear or reflex", concave.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => Shape2D.Polygon(
            [new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(8f, 0f)]));
    }

    [Fact]
    public void Polygon_RejectsPointsThatNearlyCoincide()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Shape2D.Polygon(
            [new Vector2(0f, 0f), new Vector2(0f, 0.001f), new Vector2(8f, 8f)]));

        Assert.Contains("closer together than the linear slop", error.Message, StringComparison.Ordinal);
    }

    // Winding decides which way edge normals point, so the same outline authored backwards must
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
            world.Add(Shape2D.Polygon(points), Vector2.Zero, world.Layer("target"), CollisionFilter.None);

            Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D hit));

            return (hit.Distance, hit.Normal);
        }
    }

    [Fact]
    public void Bounds_CoverThePointsAndTheRadius()
    {
        Shape2D capsule = Shape2D.Capsule(new Vector2(4f, 4f), new Vector2(4f, 12f), 3f);

        Assert.Equal(new Vector2(1f, 1f), capsule.Bounds.Min);
        Assert.Equal(new Vector2(7f, 15f), capsule.Bounds.Max);
    }

    [Fact]
    public void Translated_MovesEveryPointAndTheBounds()
    {
        Shape2D moved = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)).Translated(new Vector2(10f, 20f));

        Assert.Equal(new Vector2(10f, 20f), moved.Bounds.Min);
        Assert.Equal(new Vector2(18f, 28f), moved.Bounds.Max);
    }

    // About the shape's own origin, so a box offset from it moves with its corners rather than
    // growing in place.
    [Fact]
    public void Scaled_MultipliesEveryPointAboutTheShapesOwnOrigin()
    {
        Shape2D scaled = Shape2D.Box(new Vector2(2f, 4f), new Vector2(8f, 8f)).Scaled(new Vector2(2f, 0.5f));

        Assert.Equal(new Vector2(4f, 2f), scaled.Bounds.Min);
        Assert.Equal(new Vector2(20f, 6f), scaled.Bounds.Max);
        Assert.Equal(ShapeKind2D.Box, scaled.Kind);
    }

    [Fact]
    public void Scaled_TakesAPolygonsPointsAndAxesIndependently()
    {
        Shape2D scaled = Shape2D.Polygon([new Vector2(0f, -4f), new Vector2(8f, 0f), new Vector2(0f, 4f)])
            .Scaled(new Vector2(0.5f, 3f));

        Assert.Equal(ShapeKind2D.Polygon, scaled.Kind);
        Assert.Equal(new Vector2(0f, -12f), scaled.Point(0));
        Assert.Equal(new Vector2(4f, 0f), scaled.Point(1));
        Assert.Equal(new Vector2(0f, 12f), scaled.Point(2));
    }

    // A radius is one distance, so a non-uniform scale would name a shape the narrowphase has no
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

        Assert.Contains("A Circle is rounded by a radius", circle.Message, StringComparison.Ordinal);
        Assert.Contains("A Capsule is rounded by a radius", capsule.Message, StringComparison.Ordinal);
        Assert.Contains("A Polygon is rounded by a radius", rounded.Message, StringComparison.Ordinal);
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
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)).Scaled(new Vector2(1e-6f, 1e-6f)));

        Assert.Contains("closer together than the linear slop", error.Message, StringComparison.Ordinal);
    }

    // Scaled far enough up, a capsule's endpoints are still floats while the segment the
    // narrowphase measures along is not.
    [Fact]
    public void Scaled_RefusesACapsuleWhoseSegmentOverflows()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Shape2D.Capsule(Vector2.Zero, new Vector2(1f, 0f), 1e-6f).Scaled(new Vector2(3e19f, 3e19f)));

        Assert.Contains("The edge leaving point 0 spans more than a float can measure", error.Message, StringComparison.Ordinal);
    }
}

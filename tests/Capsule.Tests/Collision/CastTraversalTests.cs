using System.Numerics;
using Capsule.Collision;
using Capsule.Tests.Performance;

namespace Capsule.Tests.Collision;

/// <summary>
/// What a map-length grid traversal costs in the one quantity that reads the same on every machine:
/// the cells it reaches. A sweep walks the band its shape covers, which is O(width); the rectangle
/// its bounds describe is O(width x height).
/// </summary>
public sealed class CastTraversalTests
{
    private const int TileSize = CollisionWorkload.TileSize;
    private const int TilesWide = CollisionWorkload.TilesWide;
    private const int TilesHigh = CollisionWorkload.TilesHigh;

    private const float Across = TilesWide * TileSize;
    private const float Down = TilesHigh * TileSize;

    // The body the performance workload sweeps across the same map.
    private const float MoverWidth = 12f;
    private const float MoverHeight = 24f;

    // A quarter of the map is already a collapse. Only a diagonal can make the claim: its bounds
    // describe every one of the TilesWide x TilesHigh cells, while an honest band comes in an order
    // of magnitude under. A flat sweep's bounds are its own band, so it asserts the band alone.
    private const long RectangleCells = TilesWide * TilesHigh;

    [Fact]
    public void ShapeCast_AcrossTheMapDiagonal_TestsTheBandItSweepsAndNotItsBoundingRectangle()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.Filter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);

        world.ResetDiagnostics();
        world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(MoverWidth, MoverHeight)),
            Vector2.Zero,
            new Vector2(Across, Down),
            filter,
            out _);

        AssertBand(world.GridCellsTested, BandCells(MoverWidth, MoverHeight, Down / Across), "a map-length diagonal cast");
        AssertNotTheRectangle(world.GridCellsTested, "a map-length diagonal cast");
    }

    [Fact]
    public void ShapeCast_AcrossTheFullWidth_TestsOnlyTheRowsItsShapeCovers()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.Filter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);

        world.ResetDiagnostics();
        world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(MoverWidth, MoverHeight)),
            new Vector2(0f, (33f * TileSize) + 4f),
            new Vector2(Across, 0f),
            filter,
            out _);

        AssertBand(world.GridCellsTested, BandCells(MoverWidth, MoverHeight, 0f), "a map-length flat cast");
    }

    [Fact]
    public void Raycast_AcrossTheMapDiagonal_TestsOnlyTheCellsTheRayCrosses()
    {
        CollisionWorld2D world = CollisionWorkload.World();

        // Only the platform layer, and the ray climbs, so nothing stops it: the walk runs the
        // length of the grid rather than ending at a first hit that would measure nothing.
        CollisionFilter filter = world.Filter(CollisionWorkload.Platform);

        world.ResetDiagnostics();
        Assert.False(world.Raycast(
            new Vector2(0.5f, Down - 0.5f),
            new Vector2(Across, -Down),
            MathF.Sqrt((Across * Across) + (Down * Down)),
            filter,
            out _));

        AssertBand(world.GridCellsTested, BandCells(0f, 0f, Down / Across), "a map-length diagonal ray");
        AssertNotTheRectangle(world.GridCellsTested, "a map-length diagonal ray");
    }

    // The band a sweep covers, derived rather than measured. The box is inside a TileSize-wide
    // column over TileSize + width of travel in X, rising (TileSize + width) * slope over that
    // stretch, so it occupies height + (TileSize + width) * slope of Y there. A span that long
    // crosses at most ceil(span / TileSize) boundaries, plus the row it starts inside.
    private static long BandCells(float width, float height, float slope)
    {
        float span = height + ((TileSize + width) * slope);
        long rowsPerColumn = (long)MathF.Ceiling(span / TileSize) + 1;

        return rowsPerColumn * TilesWide;
    }

    private static void AssertBand(long tested, long band, string what)
    {
        Assert.True(
            tested <= band,
            FormattableString.Invariant(
                $"{what} tested {tested} grid cells against a band of {band}, so it is covering more of each column than the shape sweeps."));
    }

    private static void AssertNotTheRectangle(long tested, string what)
    {
        Assert.True(
            tested < RectangleCells / 4,
            FormattableString.Invariant(
                $"{what} tested {tested} grid cells, within reach of the {RectangleCells} its swept bounds describe, so the traversal has collapsed to the rectangle."));
    }
}

using System.Numerics;
using Capsule.Collision;
using Capsule.Tests.Performance;

namespace Capsule.Tests.Collision;

/// <summary>
/// What a map-length grid traversal costs, in the one quantity that reads the same on every
/// machine: the cells it hands to a narrowphase test. A sweep walks the band its shape covers and
/// is O(width) for a map-length cast; the rectangle its bounds describe is O(width x height), and
/// a traversal that collapsed to it would test the whole map.
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

    // A quarter of the map is already a collapse. Only a sweep whose bounds span the whole map can
    // make that claim: a diagonal corner to corner describes every one of the TilesWide x TilesHigh
    // cells, so a traversal that walked its rectangle tests all of them and an honest band fails
    // this by an order of magnitude. A flat sweep's bounds are its own band, so it has no
    // rectangle to collapse to and asserts the band alone.
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

        // Only the platform layer, and the ray climbs: a solid cell is filtered out and a top face
        // stops nothing travelling away from it, so the walk runs the length of the grid instead of
        // ending at its first hit. A ray that stopped early would say nothing about the traversal.
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

    // The band a sweep covers, derived from the geometry rather than measured. A column is a slab
    // TileSize wide: the swept box is inside it from the moment its leading edge reaches the near
    // face until its trailing edge passes the far one, which is TileSize + width of travel in X.
    // Over that stretch the sweep rises or falls (TileSize + width) * slope, so within the column
    // the box occupies height + (TileSize + width) * slope world units of Y. A span that long
    // crosses at most ceil(span / TileSize) cell boundaries, and the rows it enters are the
    // boundaries it crosses plus the one it starts inside. A ray is the same derivation with no
    // width and no height, leaving the rows the slope alone accounts for.
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

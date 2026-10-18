using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

// The grid walk: which cell a ray reaches first, and which of its faces is a surface.
public sealed class GridRaycastTests
{
    [Fact]
    public void Raycast_StopsAtTheFirstSolidCellTheGridWalkReaches()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(
            world,
            ".....",
            ".....",
            "...#.");

        Assert.True(world.Raycast(new Vector2(56f, 8f), Vector2.UnitY, 200f, CollisionFilter.Everything, out RayHit2D downwards));
        Assert.True(downwards.Target.IsGridCell);
        Assert.Equal((3, 2), (downwards.Target.CellX, downwards.Target.CellY));
        Assert.Equal(24f, downwards.Distance, 3);
        Assert.Equal(new Vector2(0f, -1f), downwards.Normal);
    }

    // A diagonal through a corner enters two solid cells at once. The nearest hit and the first of
    // every hit name the same cell, the one the total order puts first.
    [Fact]
    public void Raycast_ThroughACornerSharedByTwoSolidCells_NamesTheCellRaycastAllPutsFirst()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, ".#", "##");
        Vector2 origin = new(8f, 8f);
        Span<RayHit2D> hits = stackalloc RayHit2D[4];

        Assert.True(world.Raycast(origin, Vector2.One, 64f, CollisionFilter.Everything, out RayHit2D nearest));
        Assert.True(world.RaycastAll(origin, Vector2.One, 64f, CollisionFilter.Everything, hits) >= 2);

        Assert.Equal(hits[0].Distance, hits[1].Distance);
        Assert.Equal(hits[0].Target, nearest.Target);
        Assert.Equal((1, 0), (nearest.Target.CellX, nearest.Target.CellY));
    }

    [Fact]
    public void Raycast_NeverReportsAFaceSharedWithAnotherSolidCell()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "####", "####");

        Assert.True(world.Raycast(new Vector2(-8f, 8f), Vector2.UnitX, 200f, CollisionFilter.Everything, out RayHit2D hit));

        Assert.Equal((0, 0), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(8f, hit.Distance, 3);
        Assert.Equal(new Vector2(-1f, 0f), hit.Normal);
    }

    // A one-way edge is crossed only from above. The ray starts 16 units above the line, then turns
    // round inside the cell, and finally runs along the line itself, which is no crossing either.
    [Fact]
    public void Raycast_CrossesAOneWayEdgeOnlyFromAbove()
    {
        CollisionWorld2D world = CollisionFixtures.OneWay();

        Assert.True(world.Raycast(new Vector2(24f, 0f), Vector2.UnitY, 64f, CollisionFilter.Everything, out RayHit2D hit));
        Assert.Equal(16f, hit.Distance, 3);
        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
        Assert.Equal(world.Layer(CollisionFixtures.Ledge), hit.Target.Layer);

        Assert.False(world.Raycast(new Vector2(24f, 30f), -Vector2.UnitY, 64f, CollisionFilter.Everything, out _));
        Assert.False(world.Raycast(new Vector2(0f, 16f), Vector2.UnitX, 64f, CollisionFilter.Everything, out _));
    }

    // A diagonal reaching a cell corner dead on touches both cells it separates. The walk must not
    // commit to one and report nothing because the face it picked is the seam between the two.
    [Theory]
    [InlineData(8f, 8f, 1f, 1f)]
    [InlineData(56f, 8f, -1f, 1f)]
    public void Raycast_FindsTheExposedFaceWhereADiagonalCrossesExactlyThroughACellCorner(
        float originX,
        float originY,
        float directionX,
        float directionY)
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Assert.True(world.Raycast(
            new Vector2(originX, originY),
            new Vector2(directionX, directionY),
            200f,
            CollisionFilter.Everything,
            out RayHit2D hit));

        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
        Assert.Equal(16f, hit.Point.Y, 3);
        Assert.Equal(1, hit.Target.CellY);
    }

    // Upwards through the same corner: the underside of the run is one flat face, and the seam between
    // two of its cells is not a surface however exactly the ray meets it.
    [Fact]
    public void Raycast_CrossingACornerFromBelowMeetsTheUndersideRatherThanTheSeam()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "####", "....");

        Assert.True(world.Raycast(new Vector2(8f, 24f), new Vector2(1f, -1f), 200f, CollisionFilter.Everything, out RayHit2D hit));

        Assert.Equal(new Vector2(0f, 1f), hit.Normal);
        Assert.Equal(16f, hit.Point.Y, 3);
        Assert.Equal(0, hit.Target.CellY);
    }

    // A corner buried inside the terrain exposes nothing. The ray meets the outside of the block.
    [Fact]
    public void Raycast_DiagonalIntoABlock_HitsItsExposedOuterCornerRatherThanAnInteriorOne()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", ".###", ".###");

        Assert.True(world.Raycast(new Vector2(8f, 8f), new Vector2(1f, 1f), 200f, CollisionFilter.Everything, out RayHit2D hit));

        // The block's own outer corner at (16, 16), not the interior seam at (32, 32).
        Assert.Equal(new Vector2(16f, 16f), hit.Point);
        Assert.Equal((1, 1), (hit.Target.CellX, hit.Target.CellY));
    }
}

using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

public sealed class GridColliderTests
{
    [Fact]
    public void AddGrid_RejectsAGridThatDoesNotAddUp()
    {
        CollisionWorld world = new();

        Assert.Throws<ArgumentException>(() => world.AddGrid(16, 2, 2, [0, 0, 0], CollisionFixtures.Profiles));
        Assert.Throws<ArgumentException>(() => world.AddGrid(16, 1, 1, [7], CollisionFixtures.Profiles));
        Assert.Throws<ArgumentException>(() => world.AddGrid(16, 1, 1, [0], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddGrid(0, 1, 1, [0], CollisionFixtures.Profiles));
    }

    [Fact]
    public void AddGrid_SharesTheCallersCellArrayRatherThanCopyingIt()
    {
        CollisionWorld world = new();
        int[] cells = [0, 1];

        GridCollider map = world.AddGrid(16, 2, 1, cells, CollisionFixtures.Profiles);

        Assert.Equal(CellCollision.None, map.CollisionAt(0, 0));
        Assert.Equal(CellCollision.Solid, map.CollisionAt(1, 0));
        Assert.Equal(CollisionFixtures.Solid, world.NameOf(map.TagAt(1, 0)));
    }

    // A tilemap handle names a grid, not one shape standing somewhere with one tag. Every accessor
    // that would describe it as if it were says so, rather than answering about the empty slot the
    // grid happens to occupy.
    [Fact]
    public void ThePerColliderAccessors_RefuseAGridHandleRatherThanDescribingItsSlot()
    {
        CollisionWorld world = new();
        GridCollider map = CollisionFixtures.Paint(world, "....", "####");
        ColliderHandle handle = map.Handle;

        Assert.Throws<ArgumentException>(() => world.SetFilter(handle, world.Tag("wall"), CollisionFilter.Everything));
        Assert.Throws<ArgumentException>(() => world.PositionOf(handle));
        Assert.Throws<ArgumentException>(() => world.ShapeOf(handle));
        Assert.Throws<ArgumentException>(() => world.TagOf(handle));
        Assert.Throws<ArgumentException>(() => world.FilterOf(handle));
        Assert.Null(world.UserDataOf(handle));

        // The members that are about grids still take it, and the grid still answers as before.
        Assert.True(world.Contains(handle));
        Assert.Same(map, world.GridOf(handle));
        Assert.Equal(CollisionFixtures.Solid, world.NameOf(map.TagAt(0, 1)));

        // And the refused SetFilter changed nothing a tile query reads.
        Assert.True(world.Raycast(new Vector2(8f, 0f), Vector2.UnitY, 64f, world.Filter(CollisionFixtures.Solid), out RayHit hit));
        Assert.Equal((0, 1), (hit.Target.CellX, hit.Target.CellY));
    }

    [Fact]
    public void CollisionAt_RejectsACoordinateOffTheGrid()
    {
        CollisionWorld world = new();
        GridCollider map = CollisionFixtures.Paint(world, "##", "##");

        Assert.Throws<ArgumentOutOfRangeException>(() => map.CollisionAt(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.CollisionAt(0, -1));
    }

    [Fact]
    public void Raycast_StopsAtTheFirstSolidCellTheGridWalkReaches()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(
            world,
            ".....",
            ".....",
            "...#.");

        Assert.True(world.Raycast(new Vector2(56f, 8f), Vector2.UnitY, 200f, CollisionFilter.Everything, out RayHit downwards));
        Assert.True(downwards.Target.IsGridCell);
        Assert.Equal((3, 2), (downwards.Target.CellX, downwards.Target.CellY));
        Assert.Equal(24f, downwards.Distance, 3);
        Assert.Equal(new Vector2(0f, -1f), downwards.Normal);
    }

    [Fact]
    public void Raycast_MissesAnEmptyRowEntirely()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, ".....", "...#.");

        Assert.False(world.Raycast(new Vector2(8f, 8f), Vector2.UnitX, 200f, CollisionFilter.Everything, out _));
    }

    [Fact]
    public void Raycast_NeverReportsAFaceSharedWithAnotherSolidCell()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "####", "####");

        Assert.True(world.Raycast(new Vector2(-8f, 8f), Vector2.UnitX, 200f, CollisionFilter.Everything, out RayHit hit));

        Assert.Equal((0, 0), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(8f, hit.Distance, 3);
        Assert.Equal(new Vector2(-1f, 0f), hit.Normal);
    }

    [Fact]
    public void Raycast_ReachesAOneWayEdgeOnlyFromAbove()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "----");

        Assert.True(world.Raycast(new Vector2(24f, 0f), Vector2.UnitY, 64f, CollisionFilter.Everything, out RayHit above));
        Assert.Equal(16f, above.Distance, 3);
        Assert.Equal(new Vector2(0f, -1f), above.Normal);
        Assert.Equal(CollisionFixtures.OneWay, world.NameOf(above.Target.Tag));

        Assert.False(world.Raycast(new Vector2(24f, 30f), -Vector2.UnitY, 64f, CollisionFilter.Everything, out _));
        Assert.False(world.Raycast(new Vector2(0f, 20f), Vector2.UnitX, 64f, CollisionFilter.Everything, out _));
    }

    // A filter turns the cells it excludes into empty space, faces included: the seam a wall shares
    // with a wall the query cannot see is that wall's own outer face, not an interior one.
    [Fact]
    public void Raycast_ReachesTheFaceASolidCellSharesWithOneTheFilterExcludes()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "=#..");

        Assert.True(world.Raycast(new Vector2(40f, 8f), -Vector2.UnitX, 64f, CollisionFilter.Everything, out RayHit whole));
        Assert.Equal((1, 0), (whole.Target.CellX, whole.Target.CellY));
        Assert.Equal(8f, whole.Distance, 3);

        Assert.True(world.Raycast(
            new Vector2(40f, 8f),
            -Vector2.UnitX,
            64f,
            world.Filter(CollisionFixtures.Climb),
            out RayHit climbing));

        Assert.Equal((0, 0), (climbing.Target.CellX, climbing.Target.CellY));
        Assert.Equal(24f, climbing.Distance, 3);
        Assert.Equal(new Vector2(1f, 0f), climbing.Normal);
    }

    // A diagonal that reaches a cell corner dead on touches both cells that corner separates. The
    // walk must not commit to one of them and report nothing because the face it happened to pick
    // is the seam between the two.
    [Theory]
    [InlineData(8f, 8f, 1f, 1f)]
    [InlineData(56f, 8f, -1f, 1f)]
    public void Raycast_FindsTheExposedFaceWhereADiagonalCrossesExactlyThroughACellCorner(
        float originX,
        float originY,
        float directionX,
        float directionY)
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Assert.True(world.Raycast(
            new Vector2(originX, originY),
            new Vector2(directionX, directionY),
            200f,
            CollisionFilter.Everything,
            out RayHit hit));

        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
        Assert.Equal(16f, hit.Point.Y, 3);
        Assert.Equal(1, hit.Target.CellY);
    }

    // Upwards through the same corner: the underside of the run is one flat face, and the seam
    // between two of its cells is not a surface however exactly the ray meets it.
    [Fact]
    public void Raycast_CrossingACornerFromBelowMeetsTheUndersideRatherThanTheSeam()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "####", "....");

        Assert.True(world.Raycast(new Vector2(8f, 24f), new Vector2(1f, -1f), 200f, CollisionFilter.Everything, out RayHit hit));

        Assert.Equal(new Vector2(0f, 1f), hit.Normal);
        Assert.Equal(16f, hit.Point.Y, 3);
        Assert.Equal(0, hit.Target.CellY);
    }

    // The corner probe must not invent a neighbour: with only one of the two cells solid, the
    // answer is that cell's own exposed side.
    [Fact]
    public void Raycast_AtACornerWithOnlyOneSolidCell_ReportsThatCellsOwnFace()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", ".###");

        Assert.True(world.Raycast(new Vector2(8f, 8f), new Vector2(1f, 1f), 200f, CollisionFilter.Everything, out RayHit hit));

        Assert.Equal((1, 1), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(new Vector2(16f, 16f), hit.Point);
    }

    // A corner buried inside the terrain exposes nothing. The ray meets the outside of the block
    // and never reports the seam beyond it.
    [Fact]
    public void Raycast_ReportsNothingAtACornerWhoseFacesAreAllInterior()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", ".###", ".###");

        Assert.True(world.Raycast(new Vector2(8f, 8f), new Vector2(1f, 1f), 200f, CollisionFilter.Everything, out RayHit hit));

        // The block's own outer corner at (16,16), not the interior seam at (32,32).
        Assert.Equal(new Vector2(16f, 16f), hit.Point);
        Assert.Equal((1, 1), (hit.Target.CellX, hit.Target.CellY));
    }

    // The sweep has no such blind spot to fix: it covers a band of cells rather than walking a
    // single line through them, so the cell showing the exposed face is visited whatever the
    // leading corner lands on. Held here so that stays true.
    [Fact]
    public void ShapeCast_LandingItsLeadingCornerOnACellSeam_MeetsTheSurfaceRatherThanTheSeam()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Assert.True(world.ShapeCast(
            Shape.Box(Vector2.Zero, new Vector2(8f, 8f)),
            Vector2.Zero,
            new Vector2(16f, 16f),
            CollisionFilter.Everything,
            out ShapeCastHit hit));

        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
        Assert.Equal(0.5f, hit.Fraction, 3);
    }

    [Fact]
    public void Raycast_RespectsAFilterThatExcludesATileType()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "----", "####");
        CollisionFilter solidOnly = world.Filter(CollisionFixtures.Solid);

        Assert.True(world.Raycast(new Vector2(24f, 0f), Vector2.UnitY, 96f, solidOnly, out RayHit hit));

        Assert.Equal(32f, hit.Distance, 3);
        Assert.Equal(CollisionFixtures.Solid, world.NameOf(hit.Target.Tag));
    }

    // A tilemap is a collider like any other as far as the ignore argument is concerned; the grid
    // walks are the only broadphase that used not to consult it.
    [Fact]
    public void EveryVerb_PassesThroughATilemapGivenAsTheIgnoredCollider()
    {
        CollisionWorld world = new();
        GridCollider floor = CollisionFixtures.Paint(world, "....", "####");
        GridCollider ceiling = CollisionFixtures.Paint(world, "####", "....");
        ColliderHandle body = world.Add(
            Shape.Box(new Vector2(20f, 20f), new Vector2(8f, 8f)),
            Vector2.Zero,
            world.Tag(CollisionFixtures.Solid),
            CollisionFilter.None);

        // Downwards from inside the empty row: the floor is the only thing below, and it is ignored.
        Assert.False(world.Raycast(new Vector2(56f, 20f), Vector2.UnitY, 64f, CollisionFilter.Everything, out _, floor.Handle));

        // The ceiling above it is not, so the same ray upwards still lands.
        Assert.True(world.Raycast(new Vector2(56f, 20f), -Vector2.UnitY, 64f, CollisionFilter.Everything, out RayHit up, floor.Handle));
        Assert.Equal(ceiling.Handle, up.Target.Collider);

        Span<RayHit> hits = stackalloc RayHit[8];
        int count = world.RaycastAll(new Vector2(56f, 20f), Vector2.UnitY, 64f, CollisionFilter.Everything, hits, floor.Handle);
        Assert.Equal(0, count);

        // A box overlapping the floor row reports the body beside it and nothing of the floor.
        Span<Contact> contacts = stackalloc Contact[8];
        Assert.Equal(
            1,
            world.OverlapBox(CollisionFixtures.Box(20f, 18f, 12f, 12f), CollisionFilter.Everything, contacts, floor.Handle));
        Assert.Equal(body, contacts[0].Target.Collider);

        // And a move down through the floor is not stopped by it.
        MoveResult through = world.MoveBox(
            CollisionFixtures.Box(52f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default,
            floor.Handle);
        Assert.False(through.BlockedY);
        Assert.Equal(20f, through.Translation.Y, 2f * CollisionWorld.LinearSlop);

        // The same move with nothing ignored still lands on it.
        Assert.True(world.MoveBox(
            CollisionFixtures.Box(52f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default).BlockedY);

        Assert.False(world.ShapeCast(
            Shape.Box(Vector2.Zero, new Vector2(8f, 8f)),
            new Vector2(52f, 4f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            out _,
            floor.Handle));
    }

    [Fact]
    public void Overlap_ReportsEveryCellATouchingBoxCoversWithItsOwnCellAndTag()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Span<Contact> contacts = stackalloc Contact[8];
        int count = world.OverlapBox(CollisionFixtures.Box(20f, 12f, 16f, 8f), CollisionFilter.Everything, contacts);

        Assert.Equal(2, count);
        Assert.Equal((1, 1), (contacts[0].Target.CellX, contacts[0].Target.CellY));
        Assert.Equal((2, 1), (contacts[1].Target.CellX, contacts[1].Target.CellY));
        Assert.All(
            contacts[..count].ToArray(),
            contact => Assert.Equal(CollisionFixtures.Solid, world.NameOf(contact.Target.Tag)));
    }

    [Fact]
    public void Overlap_TreatsAOneWayCellAsItsEdgeRatherThanItsBody()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "----");

        Span<Contact> contacts = stackalloc Contact[8];

        // Wholly inside the cell but below its edge: the body is not the shape.
        Assert.Equal(0, world.OverlapBox(CollisionFixtures.Box(20f, 20f, 8f, 8f), CollisionFilter.Everything, contacts));

        // Straddling the edge.
        Assert.Equal(1, world.OverlapBox(CollisionFixtures.Box(20f, 12f, 8f, 8f), CollisionFilter.Everything, contacts));
    }
}

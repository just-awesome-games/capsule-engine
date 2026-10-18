using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

// What a collision grid accepts at construction, and how it answers as one collider of a world.
public sealed class CollisionGridTests
{
    [Fact]
    public void AddGrid_RejectsAGridThatDoesNotAddUp()
    {
        CollisionWorld2D world = new();

        Assert.Throws<ArgumentException>(() => world.AddGrid(16, 2, 2, [0, 0, 0], CollisionFixtures.Profiles(world)));
        Assert.Throws<ArgumentException>(() => world.AddGrid(16, 1, 1, [7], CollisionFixtures.Profiles(world)));
        Assert.Throws<ArgumentException>(() => world.AddGrid(16, 1, 1, [0], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddGrid(0, 1, 1, [0], CollisionFixtures.Profiles(world)));
    }

    // The grid reads its owner's cells in place. A written cell collides as written once it is refreshed.
    [Fact]
    public void AGridReadsItsOwnersCells_AndARefreshedCellCollidesAsWritten()
    {
        CollisionWorld2D world = new();
        int[] cells = [0, 1];

        CollisionGrid2D grid = world.AddGrid(16, 2, 1, cells, CollisionFixtures.Profiles(world));
        cells[0] = 1;
        cells[1] = 0;
        grid.Refresh(0, 0);
        grid.Refresh(1, 0);

        Assert.Equal(world.Layer(CollisionFixtures.Solid), grid.LayerOf(0, 0));
        Assert.Equal(CellState2D.None, grid.StateAt(1, 0));

        Assert.True(world.Raycast(new Vector2(8f, -8f), Vector2.UnitY, 32f, CollisionFilter.Everything, out RayHit2D hit));
        Assert.Equal((0, 0), (hit.Target.CellX, hit.Target.CellY));
        Assert.False(world.Raycast(new Vector2(24f, -8f), Vector2.UnitY, 32f, CollisionFilter.Everything, out _));
    }

    // A cell is on one layer, and a filter reaches it by naming that layer and no other.
    [Fact]
    public void ACellOnALayer_IsMatchedOnlyByAFilterNamingThatLayer()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "-");

        Assert.True(world.Raycast(
            new Vector2(8f, -8f),
            Vector2.UnitY,
            32f,
            world.CreateFilter(CollisionFixtures.Platform),
            out RayHit2D platform));
        Assert.Equal(world.Layer(CollisionFixtures.Platform), platform.Target.Layer);

        Assert.False(world.Raycast(
            new Vector2(8f, -8f),
            Vector2.UnitY,
            32f,
            world.CreateFilter(CollisionFixtures.Solid),
            out _));
    }


    [Fact]
    public void ThePerCellAccessors_RejectACoordinateOffTheGrid()
    {
        CollisionWorld2D world = new();
        CollisionGrid2D grid = CollisionFixtures.Paint(world, "##", "##");

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Refresh(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Refresh(0, -1));
    }

    // A grid is a collider like any other as far as the ignore argument is concerned.
    [Fact]
    public void EveryVerb_PassesThroughAGridGivenAsTheIgnoredCollider()
    {
        CollisionWorld2D world = new();
        CollisionGrid2D floor = CollisionFixtures.Paint(world, "....", "####");
        CollisionGrid2D ceiling = CollisionFixtures.Paint(world, "####", "....");
        ColliderHandle body = world.Add(
            Shape2D.Box(new Vector2(20f, 20f), new Vector2(8f, 8f)),
            Vector2.Zero,
            world.Layer(CollisionFixtures.Solid));

        // Downwards from inside the empty row: the floor is the only thing below, and it is ignored.
        Assert.False(world.Raycast(new Vector2(56f, 20f), Vector2.UnitY, 64f, CollisionFilter.Everything, out _, floor.Handle));

        // The ceiling above it is not. The same ray upwards still lands.
        Assert.True(world.Raycast(new Vector2(56f, 20f), -Vector2.UnitY, 64f, CollisionFilter.Everything, out RayHit2D up, floor.Handle));
        Assert.Equal(ceiling.Handle, up.Target.Collider);

        Span<RayHit2D> hits = stackalloc RayHit2D[8];
        Assert.Equal(0, world.RaycastAll(new Vector2(56f, 20f), Vector2.UnitY, 64f, CollisionFilter.Everything, hits, floor.Handle));

        // A box overlapping the floor row reports the body beside it and nothing of the floor.
        Span<Contact2D> contacts = stackalloc Contact2D[8];
        Assert.Equal(
            1,
            world.OverlapBoxAll(CollisionFixtures.Box(20f, 18f, 12f, 12f), CollisionFilter.Everything, contacts, floor.Handle));
        Assert.Equal(body, contacts[0].Target.Collider);

        // And a move down through the floor is not stopped by it.
        MoveResult2D through = world.MoveBox(
            CollisionFixtures.Box(52f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default,
            floor.Handle);
        Assert.False(through.Blocked);
        Assert.Equal(20f, through.Translation.Y, CollisionFixtures.Tolerance);

        // The same move with nothing ignored still lands on it.
        Assert.True(world.MoveBox(
            CollisionFixtures.Box(52f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default).Blocked);

        // A cast starting inside the ignored floor's row and clear of the ceiling meets nothing.
        Assert.False(world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)),
            new Vector2(52f, 18f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            out _,
            floor.Handle));
    }

    // A cell shape past four corners has an edge on at most each side of its cell, and only those edges
    // cull. The mound's right side lies flush against solid ground and is culled. Its left side and
    // underside stay surfaces, and its chamfered top lands what falls on it.
    [Fact]
    public void ACellShapePastFourCorners_CullsOnlyTheSideItShares()
    {
        CollisionWorld2D world = new();
        CollisionLayer solid = world.Layer(CollisionFixtures.Solid);
        Shape2D mound = Shape2D.Polygon([new(0f, 16f), new(0f, 12f), new(6f, 9f), new(10f, 9f), new(16f, 12f), new(16f, 16f)]);
        CollisionGrid2D grid = world.AddGrid(16, 2, 3, [0, 0, 2, 1, 0, 0], [new(null), new(solid), new(solid, mound)]);

        Assert.Equal(CellState2D.Edges | CellState2D.FaceMinX | CellState2D.FaceMaxY, grid.StateAt(0, 1));

        Assert.True(world.Raycast(new Vector2(8f, 0f), Vector2.UnitY, 64f, CollisionFilter.Everything, out RayHit2D top));
        Assert.Equal(25f, top.Distance, 3);
        Assert.True(world.Raycast(new Vector2(8f, 48f), -Vector2.UnitY, 64f, CollisionFilter.Everything, out RayHit2D under));
        Assert.Equal(16f, under.Distance, 3);
    }
}

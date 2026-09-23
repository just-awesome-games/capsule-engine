using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

// What a grid collider accepts at construction, and how it answers as one collider of a world.
public sealed class GridColliderTests
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

    // A cell on a layer with no face collides with nothing: a mistake rather than a way to spell an
    // empty cell, which is a profile on no layer.
    [Fact]
    public void AddGrid_RefusesAProfileOnLayersWithNoFaces()
    {
        CollisionWorld2D world = new();

        Assert.Throws<ArgumentException>(() => world.AddGrid(
            16,
            1,
            1,
            [0],
            [new CellProfile2D(world.Layer(CollisionFixtures.Solid), CellFaces2D.None)]));
    }

    [Fact]
    public void AddGrid_OwnsItsCellsAfterRegistration()
    {
        CollisionWorld2D world = new();
        int[] cells = [0, 1];

        GridCollider2D grid = world.AddGrid(16, 2, 1, cells, CollisionFixtures.Profiles(world));
        cells[0] = 1;
        cells[1] = 0;

        Assert.Null(grid.LayerAt(0, 0));
        Assert.Equal(world.Layer(CollisionFixtures.Solid), grid.LayerAt(1, 0));
        Assert.Equal(CellFaces2D.All, grid.FacesAt(1, 0));

        Assert.True(world.Raycast(new Vector2(24f, -8f), Vector2.UnitY, 32f, CollisionFilter.Everything, out RayHit2D hit));
        Assert.Equal((1, 0), (hit.Target.CellX, hit.Target.CellY));
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

    // A grid handle names a grid, not one shape on one layer, so every per-collider accessor refuses
    // it rather than answering about the empty slot the grid occupies.
    [Fact]
    public void ThePerColliderAccessors_RefuseAGridHandleRatherThanDescribingItsSlot()
    {
        CollisionWorld2D world = new();
        GridCollider2D grid = CollisionFixtures.Paint(world, "....", "####");
        ColliderHandle handle = grid.Handle;

        Assert.Throws<ArgumentException>(() => world.SetLayer(handle, world.Layer("wall")));
        Assert.Throws<ArgumentException>(() => world.PositionOf(handle));
        Assert.Throws<ArgumentException>(() => world.ShapeOf(handle));
        Assert.Throws<ArgumentException>(() => world.LayerOf(handle));
        Assert.Null(world.UserDataOf(handle));

        // The members that are about grids still take it, and the grid still answers as before.
        Assert.True(world.Contains(handle));
        Assert.Same(grid, world.GridOf(handle));
        Assert.Equal(world.Layer(CollisionFixtures.Solid), grid.LayerAt(0, 1));

        // And the refused SetFilter changed nothing a tile query reads.
        Assert.True(world.Raycast(new Vector2(8f, 0f), Vector2.UnitY, 64f, world.CreateFilter(CollisionFixtures.Solid), out RayHit2D hit));
        Assert.Equal((0, 1), (hit.Target.CellX, hit.Target.CellY));
    }

    [Fact]
    public void ThePerCellAccessors_RejectACoordinateOffTheGrid()
    {
        CollisionWorld2D world = new();
        GridCollider2D grid = CollisionFixtures.Paint(world, "##", "##");

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.LayerAt(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.LayerAt(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.FacesAt(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.FacesAt(0, -1));
    }

    // A grid is a collider like any other as far as the ignore argument is concerned.
    [Fact]
    public void EveryVerb_PassesThroughAGridGivenAsTheIgnoredCollider()
    {
        CollisionWorld2D world = new();
        GridCollider2D floor = CollisionFixtures.Paint(world, "....", "####");
        GridCollider2D ceiling = CollisionFixtures.Paint(world, "####", "....");
        ColliderHandle body = world.Add(
            Shape2D.Box(new Vector2(20f, 20f), new Vector2(8f, 8f)),
            Vector2.Zero,
            world.Layer(CollisionFixtures.Solid));

        // Downwards from inside the empty row: the floor is the only thing below, and it is ignored.
        Assert.False(world.Raycast(new Vector2(56f, 20f), Vector2.UnitY, 64f, CollisionFilter.Everything, out _, floor.Handle));

        // The ceiling above it is not, so the same ray upwards still lands.
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
        Assert.False(through.BlockedY);
        Assert.Equal(20f, through.Translation.Y, CollisionFixtures.Tolerance);

        // The same move with nothing ignored still lands on it.
        Assert.True(world.MoveBox(
            CollisionFixtures.Box(52f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default).BlockedY);

        Assert.False(world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)),
            new Vector2(52f, 4f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            out _,
            floor.Handle));
    }
}

using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

public sealed class RaycastTests
{
    [Fact]
    public void Raycast_ReturnsTheNearestHitAndItsSurfaceNormal()
    {
        CollisionWorld2D world = new();
        CollisionLayer wall = world.Layer("wall");
        world.Add(Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall);
        world.Add(Shape2D.Box(new Vector2(80f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall);

        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Everything, out RayHit2D hit));

        Assert.Equal(40f, hit.Distance, 3);
        Assert.Equal(new Vector2(-1f, 0f), hit.Normal);
        Assert.Equal(new Vector2(40f, 0f), hit.Point);
        Assert.Equal(wall, hit.Target.Layer);
        Assert.False(world.Raycast(Vector2.Zero, Vector2.UnitX, 39f, CollisionFilter.Everything, out _));
    }

    [Fact]
    public void Raycast_RespectsTheFilterAndTheIgnoredCollider()
    {
        CollisionWorld2D world = new();
        CollisionLayer wall = world.Layer("wall");
        CollisionLayer ghost = world.Layer("ghost");
        ColliderHandle near = world.Add(Shape2D.Box(new Vector2(10f, -8f), new Vector2(8f, 16f)), Vector2.Zero, ghost);
        world.Add(Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall);

        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Of(wall), out RayHit2D filtered));
        Assert.Equal(40f, filtered.Distance, 3);

        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Everything, out RayHit2D ignored, near));
        Assert.Equal(40f, ignored.Distance, 3);
    }


    // A ray that begins inside a collider has crossed no face, and a hit promises a unit normal, so
    // the nearest side of what it started in is the surface it names.
    [Fact]
    public void Raycast_StartingInsideAColliderReportsTheNearestSideRatherThanNoNormal()
    {
        CollisionWorld2D world = new();
        world.Add(Shape2D.Box(Vector2.Zero, new Vector2(20f, 8f)), Vector2.Zero, world.Layer("wall"));

        Assert.True(world.Raycast(new Vector2(4f, 2f), Vector2.UnitX, 40f, CollisionFilter.Everything, out RayHit2D hit));

        Assert.Equal(0f, hit.Distance);
        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
    }

    [Fact]
    public void RaycastAll_WritesEveryHitNearestFirst()
    {
        CollisionWorld2D world = new();
        CollisionLayer wall = world.Layer("wall");
        world.Add(Shape2D.Box(new Vector2(80f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall);
        world.Add(Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall);
        world.Add(Shape2D.Circle(new Vector2(120f, 0f), 6f), Vector2.Zero, wall);

        Span<RayHit2D> hits = stackalloc RayHit2D[8];
        int count = world.RaycastAll(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Everything, hits);

        Assert.Equal(3, count);
        Assert.Equal(40f, hits[0].Distance, 3);
        Assert.Equal(80f, hits[1].Distance, 3);
        Assert.Equal(114f, hits[2].Distance, 2);
    }

    // The span is a budget, not a race: the hits that survive are the nearest ones.
    [Fact]
    public void RaycastAll_KeepsTheNearestHitsWhenTheSpanCannotHoldThemAll()
    {
        CollisionWorld2D world = new();
        CollisionLayer wall = world.Layer("wall");

        // Tilemap cells are walked before colliders, and this grid's only solid cell is the farthest
        // thing on the ray.
        CollisionFixtures.Paint(world, "..........#");

        for (int index = 5; index >= 1; index--)
        {
            world.Add(
                Shape2D.Box(new Vector2(index * 20f, 0f), new Vector2(8f, 16f)),
                Vector2.Zero,
                wall);
        }

        Span<RayHit2D> hits = stackalloc RayHit2D[3];
        int count = world.RaycastAll(new Vector2(0f, 8f), Vector2.UnitX, 400f, CollisionFilter.Everything, hits);

        Assert.Equal(3, count);
        Assert.Equal(20f, hits[0].Distance, 3);
        Assert.Equal(40f, hits[1].Distance, 3);
        Assert.Equal(60f, hits[2].Distance, 3);
        Assert.All(hits[..count].ToArray(), hit => Assert.False(hit.Target.IsGridCell));
    }

    // The tie-break at one distance is total: tiles before colliders, then by slot.
    [Fact]
    public void RaycastAll_WritesTheGridCellBeforeCollidersAtTheSameDistance()
    {
        CollisionWorld2D world = new();
        CollisionGrid2D terrain = CollisionFixtures.Paint(world, "#");
        CollisionLayer item = world.Layer("item");

        // Both start on the grid's own left face at x = 0, so all three hits are at distance 4.
        ColliderHandle first = world.Add(Shape2D.Box(new Vector2(0f, 4f), new Vector2(1f, 8f)), Vector2.Zero, item);
        ColliderHandle second = world.Add(Shape2D.Box(new Vector2(0f, 4f), new Vector2(2f, 8f)), Vector2.Zero, item);

        Span<RayHit2D> hits = stackalloc RayHit2D[4];
        int count = world.RaycastAll(new Vector2(-4f, 4f), Vector2.UnitX, 20f, CollisionFilter.Everything, hits);

        Assert.Equal(3, count);
        Assert.All(hits[..count].ToArray(), hit => Assert.Equal(4f, hit.Distance, 3));
        Assert.Equal(terrain.Handle, hits[0].Target.Collider);
        Assert.True(hits[0].Target.IsGridCell);
        Assert.Equal(first, hits[1].Target.Collider);
        Assert.Equal(second, hits[2].Target.Collider);
    }

    // A ray starting inside several colliders meets them all at distance zero. The broadphase's shape
    // follows the order colliders were placed in, and the tie-break by handle does not. Neither does
    // which hits a full span keeps, nor how many a request of zero distance finds.
    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(2, 1, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(2, 0, 1)]
    public void Raycasts_StartingInsideSeveralCollidersAgreeWhateverOrderTheyWerePlacedIn(int first, int second, int third)
    {
        CollisionWorld2D world = new();
        CollisionLayer wall = world.Layer("wall");
        ColliderHandle[] boxes = new ColliderHandle[3];
        for (int box = 0; box < boxes.Length; box++)
        {
            float half = 4f + box;
            boxes[box] = world.Add(Shape2D.Box(new Vector2(-half), new Vector2(2f * half)), new Vector2(1000f * (box + 1), 0f), wall);
        }

        for (int filler = 0; filler < 8; filler++)
        {
            world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f)), new Vector2(filler * 20f, 100f), wall);
        }

        foreach (int box in (int[])[first, second, third])
        {
            world.SetPosition(boxes[box], Vector2.Zero);
        }

        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 40f, CollisionFilter.Everything, out RayHit2D nearest));
        Assert.Equal(boxes[0], nearest.Target.Collider);

        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 0f, CollisionFilter.Everything, out RayHit2D still));
        Assert.Equal(boxes[0], still.Target.Collider);

        Span<RayHit2D> full = stackalloc RayHit2D[2];
        Assert.Equal(2, world.RaycastAll(Vector2.Zero, Vector2.UnitX, 40f, CollisionFilter.Everything, full));
        Assert.Equal(boxes[..2], (ColliderHandle[])[full[0].Target.Collider, full[1].Target.Collider]);

        Span<RayHit2D> ample = stackalloc RayHit2D[8];
        Assert.Equal(3, world.RaycastAll(Vector2.Zero, Vector2.UnitX, 0f, CollisionFilter.Everything, ample));
        Assert.Equal(boxes, (ColliderHandle[])[ample[0].Target.Collider, ample[1].Target.Collider, ample[2].Target.Collider]);
    }

    [Fact]
    public void Raycast_ReachesEveryShapeTheUnionShips()
    {
        CollisionWorld2D world = new();
        world.Add(Shape2D.Circle(new Vector2(20f, 0f), 4f), Vector2.Zero, world.Layer("target"));
        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D circle));
        Assert.Equal(16f, circle.Distance, 3);

        CollisionWorld2D capsules = new();
        capsules.Add(Shape2D.Capsule(new Vector2(20f, -10f), new Vector2(20f, 10f), 3f), Vector2.Zero, capsules.Layer("target"));
        Assert.True(capsules.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D capsule));
        Assert.Equal(17f, capsule.Distance, 3);

        CollisionWorld2D polygons = new();
        polygons.Add(
            Shape2D.Polygon([new Vector2(20f, -8f), new Vector2(36f, 0f), new Vector2(20f, 8f)]),
            Vector2.Zero,
            polygons.Layer("target"));
        Assert.True(polygons.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D polygon));
        Assert.Equal(20f, polygon.Distance, 3);

        CollisionWorld2D rounded = new();
        rounded.Add(
            Shape2D.Polygon([new Vector2(20f, -8f), new Vector2(36f, 0f), new Vector2(20f, 8f)], 2f),
            Vector2.Zero,
            rounded.Layer("target"));
        Assert.True(rounded.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D roundedHit));
        Assert.Equal(18f, roundedHit.Distance, 3);
    }

    [Fact]
    public void Raycast_RejectsADirectionOrDistanceThatIsNotAValidRay()
    {
        CollisionWorld2D world = new();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Raycast(Vector2.Zero, Vector2.Zero, 10f, CollisionFilter.Everything, out _));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Raycast(Vector2.Zero, Vector2.UnitX, -1f, CollisionFilter.Everything, out _));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Raycast(Vector2.Zero, Vector2.UnitX, float.NaN, CollisionFilter.Everything, out _));
    }
}

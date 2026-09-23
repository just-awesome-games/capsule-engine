using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

public sealed class OverlapTests
{
    [Fact]
    public void Overlap_FindsEveryShapeTheQueryTouchesAndOrdersCollidersByHandle()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
        ColliderHandle first = world.Add(Shape2D.Circle(new Vector2(4f, 4f), 4f), Vector2.Zero, item);
        ColliderHandle second = world.Add(Shape2D.Box(new Vector2(6f, 0f), new Vector2(8f, 8f)), Vector2.Zero, item);
        world.Add(Shape2D.Circle(new Vector2(400f, 400f), 4f), Vector2.Zero, item);

        Span<Contact2D> contacts = stackalloc Contact2D[8];
        int count = world.OverlapAll(Shape2D.Box(Vector2.Zero, new Vector2(10f, 10f)), Vector2.Zero, CollisionFilter.Everything, contacts);

        Assert.Equal(2, count);
        Assert.Equal(first, contacts[0].Target.Collider);
        Assert.Equal(second, contacts[1].Target.Collider);
    }

    // The span is the destination, never the question: the count is what the world holds, and the
    // handle order decides which of them fit, so an unrelated move cannot change the answer.
    [Fact]
    public void OverlapAll_ReportsEveryOverlapAndKeepsTheSameFewWhicheverOrderItMetThem()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
        ColliderHandle[] handles = new ColliderHandle[6];
        for (int index = 0; index < handles.Length; index++)
        {
            handles[index] = world.Add(
                Shape2D.Box(new Vector2(index, 0f), new Vector2(8f, 8f)),
                Vector2.Zero,
                item);
        }

        Aabb2D probe = CollisionFixtures.Box(0f, 0f, 8f, 8f);
        Span<Contact2D> room = stackalloc Contact2D[2];

        Assert.Equal(6, world.OverlapBoxAll(probe, CollisionFilter.Everything, room));
        Assert.Equal(handles[0], room[0].Target.Collider);
        Assert.Equal(handles[1], room[1].Target.Collider);

        // Moving every collider rearranges the broadphase, and the same two survive the span.
        for (int index = handles.Length - 1; index >= 0; index--)
        {
            world.SetPosition(handles[index], new Vector2(0.25f * index, 0f));
        }

        Assert.Equal(6, world.OverlapBoxAll(probe, CollisionFilter.Everything, room));
        Assert.Equal(handles[0], room[0].Target.Collider);
        Assert.Equal(handles[1], room[1].Target.Collider);

        // An empty span still counts them.
        Assert.Equal(6, world.OverlapBoxAll(probe, CollisionFilter.Everything, default));
    }

    [Fact]
    public void Overlap_RespectsTheFilterAndTheIgnoredCollider()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
        CollisionLayer other = world.Layer("other");
        ColliderHandle self = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, item);
        world.Add(Shape2D.Box(new Vector2(4f, 0f), new Vector2(8f, 8f)), Vector2.Zero, other);

        Span<Contact2D> contacts = stackalloc Contact2D[8];

        Assert.Equal(1, world.OverlapAll(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, CollisionFilter.Everything, contacts, self));
        Assert.Equal(1, world.OverlapAll(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, CollisionFilter.Of(item), contacts));
        Assert.Equal(0, world.OverlapAll(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, CollisionFilter.Of(item), contacts, self));
    }

    // A collider's own query takes the filter the caller hands it, so the mask the collider was
    // registered with is a default and never an override.
    [Fact]
    public void OverlapColliderAll_MatchesTheFilterItIsGivenRatherThanTheCollidersOwn()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
        CollisionLayer other = world.Layer("other");
        ColliderHandle self = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, item);
        world.Add(Shape2D.Box(new Vector2(4f, 0f), new Vector2(8f, 8f)), Vector2.Zero, other);

        Span<Contact2D> contacts = stackalloc Contact2D[8];

        Assert.Equal(1, world.OverlapColliderAll(self, CollisionFilter.Of(other), contacts));
        Assert.Equal(0, world.OverlapColliderAll(self, CollisionFilter.Of(item), contacts));
        Assert.Equal(1, world.OverlapColliderAll(self, CollisionFilter.Everything, contacts));
    }

    [Fact]
    public void Overlap_WritesTheGridCellBeforeTheColliderItAlsoTouches()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "#");
        ColliderHandle item = world.Add(
            Shape2D.Box(new Vector2(4f, 4f), new Vector2(4f, 4f)),
            Vector2.Zero,
            world.Layer("item"));

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        int count = world.OverlapAll(
            Shape2D.Box(new Vector2(4f, 4f), new Vector2(8f, 8f)),
            Vector2.Zero,
            CollisionFilter.Everything,
            contacts);

        Assert.Equal(2, count);
        Assert.True(contacts[0].Target.IsGridCell);
        Assert.Equal((0, 0), (contacts[0].Target.CellX, contacts[0].Target.CellY));
        Assert.Equal(item, contacts[1].Target.Collider);
    }

    [Fact]
    public void ShapeCast_StopsAtTheFirstThingAlongTheTranslation()
    {
        CollisionWorld2D world = new();
        world.Add(Shape2D.Box(new Vector2(100f, 0f), new Vector2(16f, 16f)), Vector2.Zero, world.Layer("wall"));

        Assert.True(world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(10f, 10f)),
            new Vector2(0f, 3f),
            new Vector2(200f, 0f),
            CollisionFilter.Everything,
            out ShapeCastHit2D hit));

        Assert.Equal(0.45f, hit.Fraction, 3);
        Assert.Equal(new Vector2(-1f, 0f), hit.Normal);
    }

    [Fact]
    public void ShapeCast_MissesWhatTheTranslationDoesNotReach()
    {
        CollisionWorld2D world = new();
        world.Add(Shape2D.Box(new Vector2(100f, 0f), new Vector2(16f, 16f)), Vector2.Zero, world.Layer("wall"));

        Assert.False(world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(10f, 10f)),
            Vector2.Zero,
            new Vector2(80f, 0f),
            CollisionFilter.Everything,
            out _));
    }

    // Depth is the way out: stepping the query by Normal * Depth leaves it touching and no deeper.
    // One box overlaps another by 3 across X and a lone solid cell by 2 down Y.
    [Fact]
    public void AnOverlap_ReportsItsDepth_AndMovingByNormalTimesDepthLeavesTheShapesTouching()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "...", ".#.", "...");
        world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), new Vector2(60f, 0f), world.Layer("item"));
        Span<Contact2D> contacts = stackalloc Contact2D[4];

        Assert.Equal(1, world.OverlapBoxAll(CollisionFixtures.Box(55f, 0f, 8f, 8f), CollisionFilter.Everything, contacts));
        Assert.Equal(new Vector2(-1f, 0f), contacts[0].Normal);
        Assert.Equal(3f, contacts[0].Depth, CollisionFixtures.Tolerance);

        Vector2 outOfBox = contacts[0].Normal * contacts[0].Depth;
        Assert.Equal(1, world.OverlapBoxAll(CollisionFixtures.Box(55f + outOfBox.X, 0f, 8f, 8f), CollisionFilter.Everything, contacts));
        Assert.Equal(0f, contacts[0].Depth);

        Assert.Equal(1, world.OverlapBoxAll(CollisionFixtures.Box(20f, 10f, 8f, 8f), CollisionFilter.Everything, contacts));
        Assert.Equal(new Vector2(0f, -1f), contacts[0].Normal);
        Assert.Equal(2f, contacts[0].Depth, CollisionFixtures.Tolerance);

        Vector2 outOfCell = contacts[0].Normal * contacts[0].Depth;
        Assert.Equal(1, world.OverlapBoxAll(CollisionFixtures.Box(20f, 10f + outOfCell.Y, 8f, 8f), CollisionFilter.Everything, contacts));
        Assert.Equal(0f, contacts[0].Depth);
    }
}

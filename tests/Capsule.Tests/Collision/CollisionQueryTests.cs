using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

public sealed class CollisionQueryTests
{
    [Fact]
    public void Raycast_ReturnsTheNearestHitAndItsSurfaceNormal()
    {
        CollisionWorld2D world = new();
        CollisionTag wall = world.Tag("wall");
        world.Add(Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall, CollisionFilter.None);
        world.Add(Shape2D.Box(new Vector2(80f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall, CollisionFilter.None);

        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Everything, out RayHit2D hit));

        Assert.Equal(40f, hit.Distance, 3);
        Assert.Equal(new Vector2(-1f, 0f), hit.Normal);
        Assert.Equal(new Vector2(40f, 0f), hit.Point);
        Assert.Equal(wall, hit.Target.Tag);
    }

    [Fact]
    public void Raycast_RespectsTheFilterAndTheIgnoredCollider()
    {
        CollisionWorld2D world = new();
        CollisionTag wall = world.Tag("wall");
        CollisionTag ghost = world.Tag("ghost");
        ColliderHandle near = world.Add(Shape2D.Box(new Vector2(10f, -8f), new Vector2(8f, 16f)), Vector2.Zero, ghost, CollisionFilter.None);
        world.Add(Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall, CollisionFilter.None);

        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Of(wall), out RayHit2D filtered));
        Assert.Equal(40f, filtered.Distance, 3);

        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Everything, out RayHit2D ignored, near));
        Assert.Equal(40f, ignored.Distance, 3);
    }

    [Fact]
    public void Raycast_StopsShortOfSomethingBeyondItsDistance()
    {
        CollisionWorld2D world = new();
        world.Add(Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f)), Vector2.Zero, world.Tag("wall"), CollisionFilter.None);

        Assert.False(world.Raycast(Vector2.Zero, Vector2.UnitX, 39f, CollisionFilter.Everything, out _));
    }

    [Fact]
    public void RaycastAll_WritesEveryHitNearestFirstAndNeverPastTheSpan()
    {
        CollisionWorld2D world = new();
        CollisionTag wall = world.Tag("wall");
        world.Add(Shape2D.Box(new Vector2(80f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall, CollisionFilter.None);
        world.Add(Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f)), Vector2.Zero, wall, CollisionFilter.None);
        world.Add(Shape2D.Circle(new Vector2(120f, 0f), 6f), Vector2.Zero, wall, CollisionFilter.None);

        Span<RayHit2D> hits = stackalloc RayHit2D[8];
        int count = world.RaycastAll(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Everything, hits);

        Assert.Equal(3, count);
        Assert.Equal(40f, hits[0].Distance, 3);
        Assert.Equal(80f, hits[1].Distance, 3);
        Assert.Equal(114f, hits[2].Distance, 2);

        Span<RayHit2D> narrow = stackalloc RayHit2D[2];
        Assert.Equal(2, world.RaycastAll(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Everything, narrow));
    }

    // The span is a budget, not a race: whichever hits the traversal happens to meet first, the
    // ones that survive are the nearest ones.
    [Fact]
    public void RaycastAll_KeepsTheNearestHitsWhenTheSpanCannotHoldThemAll()
    {
        CollisionWorld2D world = new();
        CollisionTag wall = world.Tag("wall");

        // Tilemap cells are walked before colliders, and this grid's only solid cell is the
        // farthest thing on the ray.
        CollisionFixtures.Paint(world, "..........#");

        for (int index = 5; index >= 1; index--)
        {
            world.Add(
                Shape2D.Box(new Vector2(index * 20f, 0f), new Vector2(8f, 16f)),
                Vector2.Zero,
                wall,
                CollisionFilter.None);
        }

        Span<RayHit2D> hits = stackalloc RayHit2D[3];
        int count = world.RaycastAll(new Vector2(0f, 8f), Vector2.UnitX, 400f, CollisionFilter.Everything, hits);

        Assert.Equal(3, count);
        Assert.Equal(20f, hits[0].Distance, 3);
        Assert.Equal(40f, hits[1].Distance, 3);
        Assert.Equal(60f, hits[2].Distance, 3);
        Assert.All(hits[..count].ToArray(), hit => Assert.False(hit.Target.IsGridCell));
    }

    [Fact]
    public void AWorld_RefusesAHandleOrTagThatCameFromAnotherWorld()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        GridCollider2D terrain = CollisionFixtures.Paint(first, "##");
        ColliderHandle foreign = first.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, first.Tag("item"), CollisionFilter.None);
        ColliderHandle own = second.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, second.Tag("item"), CollisionFilter.None);

        // Same slot, same generation, different world: identity has to say so.
        Assert.NotEqual(foreign, own);
        Assert.NotEqual(first.Tag("item"), second.Tag("item"));

        Assert.Throws<ArgumentException>(() => second.Contains(foreign));
        Assert.Throws<ArgumentException>(() => second.Remove(foreign));
        Assert.Throws<ArgumentException>(() => second.PositionOf(foreign));
        Assert.Throws<ArgumentException>(() => second.GridOf(foreign));
        Assert.Throws<ArgumentException>(() => second.Remove(terrain));
        Assert.Throws<ArgumentException>(() => second.NameOf(first.Tag("item")));
        Assert.Throws<ArgumentException>(() => second.NameOf(terrain.TagAt(0, 0)));
        Assert.Throws<ArgumentException>(
            () => second.Add(Shape2D.Box(Vector2.Zero, new Vector2(4f, 4f)), Vector2.Zero, first.Tag("item"), CollisionFilter.None));
        Assert.Throws<ArgumentException>(
            () => second.Raycast(Vector2.Zero, Vector2.UnitX, 10f, CollisionFilter.Everything, out _, foreign));
        Assert.Throws<ArgumentException>(
            () => second.OverlapBox(CollisionFixtures.Box(0f, 0f, 8f, 8f), CollisionFilter.Everything, default, foreign));

        // The collider it did add is untouched by any of that.
        Assert.True(second.Contains(own));
        Assert.Equal(1, second.ColliderCount);
    }

    [Fact]
    public void EveryFilterSeam_RefusesAFilterBuiltFromAnotherWorldsTags()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        CollisionFilter foreign = first.Filter("wall");
        CollisionTag item = second.Tag("item");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        ColliderHandle handle = second.Add(box, Vector2.Zero, item, CollisionFilter.None);
        Aabb2D probe = CollisionFixtures.Box(0f, 0f, 8f, 8f);

        Assert.Throws<ArgumentException>(() => second.Add(box, Vector2.Zero, item, foreign));
        Assert.Throws<ArgumentException>(() => second.SetFilter(handle, item, foreign));
        Assert.Throws<ArgumentException>(() => second.Raycast(Vector2.Zero, Vector2.UnitX, 10f, foreign, out _));
        Assert.Throws<ArgumentException>(() => second.RaycastAll(Vector2.Zero, Vector2.UnitX, 10f, foreign, default));
        Assert.Throws<ArgumentException>(() => second.ShapeCast(box, Vector2.Zero, new Vector2(10f, 0f), foreign, out _));
        Assert.Throws<ArgumentException>(() => second.Overlap(box, Vector2.Zero, foreign, default));
        Assert.Throws<ArgumentException>(() => second.OverlapBox(probe, foreign, default));
        Assert.Throws<ArgumentException>(() => second.Move(box, Vector2.Zero, new Vector2(10f, 0f), foreign, default));
        Assert.Throws<ArgumentException>(() => second.MoveBox(probe, new Vector2(10f, 0f), foreign, default));

        // None of that disturbed the filter the collider was actually registered with.
        Assert.Equal(CollisionFilter.None, second.FilterOf(handle));
    }

    // One NaN bound in the tree does not stay in its own proxy: the tree unions boxes as it
    // balances, so it would spread to ancestors shared with colliders that have nothing to do with
    // it. Nothing non-finite is allowed to reach a proxy in the first place.
    [Fact]
    public void EveryTransformSeam_RefusesANonFiniteValueAndLeavesTheWorldQueryable()
    {
        CollisionWorld2D world = new();
        CollisionTag item = world.Tag("item");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        ColliderHandle bystander = world.Add(box, new Vector2(100f, 0f), item, CollisionFilter.None);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Add(box, new Vector2(float.NaN, 0f), item, CollisionFilter.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Add(box, new Vector2(0f, float.PositiveInfinity), item, CollisionFilter.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.SetPosition(bystander, new Vector2(float.NaN, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.ShapeCast(box, new Vector2(float.NaN, 0f), Vector2.UnitX, CollisionFilter.Everything, out _));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.ShapeCast(box, Vector2.Zero, new Vector2(float.PositiveInfinity, 0f), CollisionFilter.Everything, out _));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Overlap(box, new Vector2(0f, float.NaN), CollisionFilter.Everything, default));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Move(box, new Vector2(float.NaN, 0f), Vector2.UnitX, CollisionFilter.Everything, default));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.MoveBox(CollisionFixtures.Box(0f, 0f, 8f, 8f), new Vector2(0f, float.NegativeInfinity), CollisionFilter.Everything, default));

        // The collider that was already there is untouched, still where it was, and still found.
        Assert.Equal(1, world.ColliderCount);
        Assert.Equal(new Vector2(100f, 0f), world.PositionOf(bystander));

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(1, world.OverlapBox(CollisionFixtures.Box(100f, 0f, 8f, 8f), CollisionFilter.Everything, contacts));
        Assert.Equal(bystander, contacts[0].Target.Collider);
    }

    // Both endpoints of a move can be perfectly good floats while the step between them is not.
    [Fact]
    public void EveryDerivedTransform_IsCheckedTooAndLeavesTheWorldQueryable()
    {
        CollisionWorld2D world = new();
        CollisionTag item = world.Tag("item");

        // Wide enough to still have extent out there: a small shape cannot reach these coordinates
        // at all, because the floats either side of it are the same float.
        Shape2D wide = Shape2D.Box(Vector2.Zero, new Vector2(1e38f, 1e38f));
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        ColliderHandle far = world.Add(wide, new Vector2(-2e38f, 0f), item, CollisionFilter.None);
        ColliderHandle bystander = world.Add(box, new Vector2(100f, 0f), item, CollisionFilter.None);

        // Both positions are finite; the displacement between them is not.
        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetPosition(far, new Vector2(2e38f, 0f)));

        // Shape2D and translation are each finite; the box the sweep covers between them is not.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.ShapeCast(wide, new Vector2(2e38f, 0f), new Vector2(2e38f, 0f), CollisionFilter.Everything, out _));

        // And the mover refuses the same journey by its endpoint, before it steps an axis of it.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Move(wide, new Vector2(2e38f, 0f), new Vector2(2e38f, 0f), CollisionFilter.Everything, default));

        // Nothing moved, and the collider on the other side of the world is still found.
        Assert.Equal(new Vector2(-2e38f, 0f), world.PositionOf(far));

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(1, world.OverlapBox(CollisionFixtures.Box(100f, 0f, 8f, 8f), CollisionFilter.Everything, contacts));
        Assert.Equal(bystander, contacts[0].Target.Collider);
    }

    // A handle to a removed collider carries an index the world has since handed to somebody else,
    // so honouring it as an ignore would silence an unrelated collider.
    [Fact]
    public void EveryQueryVerb_RefusesAStaleIgnoreRatherThanSuppressingWhateverTookItsSlot()
    {
        CollisionWorld2D world = new();
        CollisionTag item = world.Tag("item");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        Aabb2D probe = CollisionFixtures.Box(0f, 0f, 8f, 8f);

        ColliderHandle removed = world.Add(box, Vector2.Zero, item, CollisionFilter.None);
        world.Remove(removed);

        ColliderHandle reused = world.Add(box, Vector2.Zero, item, CollisionFilter.None);
        Assert.Equal(removed.Index, reused.Index);
        Assert.NotEqual(removed, reused);

        Assert.Throws<ArgumentException>(
            () => world.Raycast(new Vector2(-10f, 4f), Vector2.UnitX, 50f, CollisionFilter.Everything, out _, removed));
        Assert.Throws<ArgumentException>(
            () => world.RaycastAll(new Vector2(-10f, 4f), Vector2.UnitX, 50f, CollisionFilter.Everything, default, removed));
        Assert.Throws<ArgumentException>(
            () => world.ShapeCast(box, new Vector2(-20f, 0f), new Vector2(40f, 0f), CollisionFilter.Everything, out _, removed));
        Assert.Throws<ArgumentException>(
            () => world.Overlap(box, Vector2.Zero, CollisionFilter.Everything, default, removed));
        Assert.Throws<ArgumentException>(() => world.OverlapBox(probe, CollisionFilter.Everything, default, removed));
        Assert.Throws<ArgumentException>(
            () => world.Move(box, Vector2.Zero, new Vector2(10f, 0f), CollisionFilter.Everything, default, removed));
        Assert.Throws<ArgumentException>(
            () => world.MoveBox(probe, new Vector2(10f, 0f), CollisionFilter.Everything, default, removed));

        // The collider that took the slot is a collider like any other, and its own handle still
        // ignores it.
        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(1, world.OverlapBox(probe, CollisionFilter.Everything, contacts));
        Assert.Equal(reused, contacts[0].Target.Collider);
        Assert.Equal(0, world.OverlapBox(probe, CollisionFilter.Everything, contacts, reused));
    }

    [Fact]
    public void AShapeWhoseBoundsOverflow_IsRefusedThoughEveryInputIsFinite()
    {
        // Each value is a real float; the box they describe is not.
        Assert.Throws<ArgumentException>(() => Shape2D.Circle(new Vector2(3e38f, 0f), 3e38f));
        Assert.Throws<ArgumentException>(
            () => Shape2D.Polygon([new Vector2(-3e38f, -1f), new Vector2(3e38f, -1f), new Vector2(0f, 1f)], 3e38f));
        // A shape that fits where it was built and not where it is being put.
        Assert.Throws<ArgumentException>(
            () => Shape2D.Circle(Vector2.Zero, 8e37f).Translated(new Vector2(3e38f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)).Translated(new Vector2(float.NaN, 0f)));
    }

    [Fact]
    public void EveryShapeSeam_RefusesADefaultShapeRatherThanActingOnAnEmptyPointSet()
    {
        CollisionWorld2D world = new();
        CollisionTag item = world.Tag("item");
        ColliderHandle handle = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, item, CollisionFilter.None);
        Shape2D none = default;

        Assert.Equal(0, none.PointCount);
        Assert.Throws<ArgumentException>(() => world.Add(none, Vector2.Zero, item, CollisionFilter.None));
        Assert.Throws<ArgumentException>(() => world.SetShape(handle, none));
        Assert.Throws<ArgumentException>(
            () => world.ShapeCast(none, Vector2.Zero, new Vector2(10f, 0f), CollisionFilter.Everything, out _));
        Assert.Throws<ArgumentException>(
            () => world.Overlap(none, Vector2.Zero, CollisionFilter.Everything, default));
        Assert.Throws<ArgumentException>(
            () => world.Move(none, Vector2.Zero, new Vector2(10f, 0f), CollisionFilter.Everything, default));
    }

    [Fact]
    public void Raycast_ReachesEveryShapeTheUnionShips()
    {
        CollisionWorld2D world = new();
        CollisionTag target = world.Tag("target");

        world.Add(Shape2D.Circle(new Vector2(20f, 0f), 4f), Vector2.Zero, target, CollisionFilter.None);
        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D circle));
        Assert.Equal(16f, circle.Distance, 3);

        CollisionWorld2D capsules = new();
        capsules.Add(Shape2D.Capsule(new Vector2(20f, -10f), new Vector2(20f, 10f), 3f), Vector2.Zero, capsules.Tag("target"), CollisionFilter.None);
        Assert.True(capsules.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D capsule));
        Assert.Equal(17f, capsule.Distance, 3);

        CollisionWorld2D polygons = new();
        polygons.Add(
            Shape2D.Polygon([new Vector2(20f, -8f), new Vector2(36f, 0f), new Vector2(20f, 8f)]),
            Vector2.Zero,
            polygons.Tag("target"),
            CollisionFilter.None);
        Assert.True(polygons.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D polygon));
        Assert.Equal(20f, polygon.Distance, 3);

        CollisionWorld2D rounded = new();
        rounded.Add(
            Shape2D.Polygon([new Vector2(20f, -8f), new Vector2(36f, 0f), new Vector2(20f, 8f)], 2f),
            Vector2.Zero,
            rounded.Tag("target"),
            CollisionFilter.None);
        Assert.True(rounded.Raycast(Vector2.Zero, Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D roundedHit));
        Assert.Equal(18f, roundedHit.Distance, 3);
    }

    [Fact]
    public void Overlap_FindsEveryShapeTheQueryTouchesAndOrdersCollidersByHandle()
    {
        CollisionWorld2D world = new();
        CollisionTag item = world.Tag("item");
        ColliderHandle first = world.Add(Shape2D.Circle(new Vector2(4f, 4f), 4f), Vector2.Zero, item, CollisionFilter.None);
        ColliderHandle second = world.Add(Shape2D.Box(new Vector2(6f, 0f), new Vector2(8f, 8f)), Vector2.Zero, item, CollisionFilter.None);
        world.Add(Shape2D.Circle(new Vector2(400f, 400f), 4f), Vector2.Zero, item, CollisionFilter.None);

        Span<Contact2D> contacts = stackalloc Contact2D[8];
        int count = world.Overlap(Shape2D.Box(Vector2.Zero, new Vector2(10f, 10f)), Vector2.Zero, CollisionFilter.Everything, contacts);

        Assert.Equal(2, count);
        Assert.Equal(first, contacts[0].Target.Collider);
        Assert.Equal(second, contacts[1].Target.Collider);
    }

    [Fact]
    public void Overlap_RespectsTheFilterAndTheIgnoredCollider()
    {
        CollisionWorld2D world = new();
        CollisionTag item = world.Tag("item");
        CollisionTag other = world.Tag("other");
        ColliderHandle self = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, item, CollisionFilter.None);
        world.Add(Shape2D.Box(new Vector2(4f, 0f), new Vector2(8f, 8f)), Vector2.Zero, other, CollisionFilter.None);

        Span<Contact2D> contacts = stackalloc Contact2D[8];

        Assert.Equal(1, world.Overlap(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, CollisionFilter.Everything, contacts, self));
        Assert.Equal(1, world.Overlap(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, CollisionFilter.Of(item), contacts));
        Assert.Equal(0, world.Overlap(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, CollisionFilter.Of(item), contacts, self));
    }

    [Fact]
    public void ShapeCast_StopsAtTheFirstThingAlongTheTranslation()
    {
        CollisionWorld2D world = new();
        CollisionTag wall = world.Tag("wall");
        world.Add(Shape2D.Box(new Vector2(100f, 0f), new Vector2(16f, 16f)), Vector2.Zero, wall, CollisionFilter.None);

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
        world.Add(Shape2D.Box(new Vector2(100f, 0f), new Vector2(16f, 16f)), Vector2.Zero, world.Tag("wall"), CollisionFilter.None);

        Assert.False(world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(10f, 10f)),
            Vector2.Zero,
            new Vector2(80f, 0f),
            CollisionFilter.Everything,
            out _));
    }

    [Fact]
    public void SetPosition_IsObservedByTheVeryNextQuery()
    {
        CollisionWorld2D world = new();
        ColliderHandle moving = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, world.Tag("item"), CollisionFilter.None);

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(0, world.OverlapBox(CollisionFixtures.Box(100f, 0f, 8f, 8f), CollisionFilter.Everything, contacts));

        world.SetPosition(moving, new Vector2(100f, 0f));

        Assert.Equal(1, world.OverlapBox(CollisionFixtures.Box(100f, 0f, 8f, 8f), CollisionFilter.Everything, contacts));
    }

    [Fact]
    public void Remove_LeavesAStaleHandleNamingNothing()
    {
        CollisionWorld2D world = new();
        ColliderHandle handle = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, world.Tag("item"), CollisionFilter.None);

        world.Remove(handle);

        Assert.False(world.Contains(handle));
        Assert.Throws<ArgumentException>(() => world.PositionOf(handle));

        ColliderHandle reused = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, world.Tag("item"), CollisionFilter.None);
        Assert.NotEqual(handle, reused);
        Assert.False(world.Contains(handle));
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

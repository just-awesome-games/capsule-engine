using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

// What every query seam refuses, and the proof that a refusal leaves the world exactly as it was.
public sealed class CollisionGuardTests
{
    [Fact]
    public void AWorld_RefusesAHandleOrLayerThatCameFromAnotherWorld()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        GridCollider2D terrain = CollisionFixtures.Paint(first, "##");
        ColliderHandle foreign = first.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, first.Layer("item"), CollisionFilter.None);
        ColliderHandle own = second.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, second.Layer("item"), CollisionFilter.None);

        // Same slot, same generation, different world: identity has to say so.
        Assert.NotEqual(foreign, own);
        Assert.NotEqual(first.Layer("item"), second.Layer("item"));

        Assert.Throws<ArgumentException>(() => second.Contains(foreign));
        Assert.Throws<ArgumentException>(() => second.Remove(foreign));
        Assert.Throws<ArgumentException>(() => second.PositionOf(foreign));
        Assert.Throws<ArgumentException>(() => second.GridOf(foreign));
        Assert.Throws<ArgumentException>(() => second.Remove(terrain));
        Assert.Throws<ArgumentException>(() => second.NameOf(first.Layer("item")));
        Assert.Throws<ArgumentException>(
            () => second.OverlapBoxAll(terrain.CellBounds(0, 0), first.CreateFilter(CollisionFixtures.Solid), default));
        Assert.Throws<ArgumentException>(
            () => second.Add(Shape2D.Box(Vector2.Zero, new Vector2(4f, 4f)), Vector2.Zero, first.Layer("item"), CollisionFilter.None));
        Assert.Throws<ArgumentException>(
            () => second.Raycast(Vector2.Zero, Vector2.UnitX, 10f, CollisionFilter.Everything, out _, foreign));
        Assert.Throws<ArgumentException>(
            () => second.OverlapBoxAll(CollisionFixtures.Box(0f, 0f, 8f, 8f), CollisionFilter.Everything, default, foreign));

        // The collider it did add is untouched by any of that.
        Assert.True(second.Contains(own));
        Assert.Equal(1, second.ColliderCount);
    }

    [Fact]
    public void EveryFilterSeam_RefusesAFilterBuiltFromAnotherWorldsLayers()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        first.Layer("wall");
        CollisionFilter foreign = first.CreateFilter("wall");
        CollisionLayer item = second.Layer("item");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        ColliderHandle handle = second.Add(box, Vector2.Zero, item, CollisionFilter.None);
        Aabb2D probe = CollisionFixtures.Box(0f, 0f, 8f, 8f);

        Assert.Throws<ArgumentException>(() => second.Add(box, Vector2.Zero, item, foreign));
        Assert.Throws<ArgumentException>(() => second.SetFilter(handle, item, foreign));
        Assert.Throws<ArgumentException>(() => second.Raycast(Vector2.Zero, Vector2.UnitX, 10f, foreign, out _));
        Assert.Throws<ArgumentException>(() => second.RaycastAll(Vector2.Zero, Vector2.UnitX, 10f, foreign, default));
        Assert.Throws<ArgumentException>(() => second.ShapeCast(box, Vector2.Zero, new Vector2(10f, 0f), foreign, out _));
        Assert.Throws<ArgumentException>(() => second.OverlapAll(box, Vector2.Zero, foreign, default));
        Assert.Throws<ArgumentException>(() => second.OverlapBoxAll(probe, foreign, default));
        Assert.Throws<ArgumentException>(() => second.OverlapColliderAll(handle, foreign, default));
        Assert.Throws<ArgumentException>(() => second.Move(box, Vector2.Zero, new Vector2(10f, 0f), foreign, default));
        Assert.Throws<ArgumentException>(() => second.MoveBox(probe, new Vector2(10f, 0f), foreign, default));

        // None of that disturbed the filter the collider was actually registered with.
        Assert.Equal(CollisionFilter.None, second.FilterOf(handle));
    }

    // One NaN bound does not stay in its own proxy: the tree unions boxes as it balances, so it would
    // spread to ancestors shared with unrelated colliders.
    [Fact]
    public void EveryTransformSeam_RefusesANonFiniteValueAndLeavesTheWorldQueryable()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
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
            () => world.OverlapAll(box, new Vector2(0f, float.NaN), CollisionFilter.Everything, default));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Move(box, new Vector2(float.NaN, 0f), Vector2.UnitX, CollisionFilter.Everything, default));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.MoveBox(CollisionFixtures.Box(0f, 0f, 8f, 8f), new Vector2(0f, float.NegativeInfinity), CollisionFilter.Everything, default));

        // The collider that was already there is untouched, still where it was, and still found.
        Assert.Equal(1, world.ColliderCount);
        Assert.Equal(new Vector2(100f, 0f), world.PositionOf(bystander));

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(1, world.OverlapBoxAll(CollisionFixtures.Box(100f, 0f, 8f, 8f), CollisionFilter.Everything, contacts));
        Assert.Equal(bystander, contacts[0].Target.Collider);
    }

    // Both ends of a journey can be perfectly good floats while the step between them is not.
    [Fact]
    public void ADerivedDisplacement_IsCheckedTooAndLeavesTheWorldQueryable()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
        Shape2D wide = Shape2D.Box(Vector2.Zero, new Vector2(1e38f, 1e38f));
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        ColliderHandle far = world.Add(wide, new Vector2(-2e38f, 0f), item, CollisionFilter.None);
        ColliderHandle bystander = world.Add(box, new Vector2(100f, 0f), item, CollisionFilter.None);

        // Both positions are finite; the displacement between them is not.
        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetPosition(far, new Vector2(2e38f, 0f)));

        // The mover refuses the same journey by its endpoint, before it steps an axis of it.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Move(wide, new Vector2(2e38f, 0f), new Vector2(2e38f, 0f), CollisionFilter.Everything, default));

        // Nothing moved, and the collider on the other side of the world is still found.
        Assert.Equal(new Vector2(-2e38f, 0f), world.PositionOf(far));

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(1, world.OverlapBoxAll(CollisionFixtures.Box(100f, 0f, 8f, 8f), CollisionFilter.Everything, contacts));
        Assert.Equal(bystander, contacts[0].Target.Collider);
    }

    // A removed collider's handle carries an index since handed to somebody else, so honouring it as
    // an ignore would silence an unrelated collider.
    [Fact]
    public void EveryQueryVerb_RefusesAStaleIgnoreRatherThanSuppressingWhateverTookItsSlot()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
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
            () => world.OverlapAll(box, Vector2.Zero, CollisionFilter.Everything, default, removed));
        Assert.Throws<ArgumentException>(() => world.OverlapBoxAll(probe, CollisionFilter.Everything, default, removed));
        Assert.Throws<ArgumentException>(
            () => world.Move(box, Vector2.Zero, new Vector2(10f, 0f), CollisionFilter.Everything, default, removed));
        Assert.Throws<ArgumentException>(
            () => world.MoveBox(probe, new Vector2(10f, 0f), CollisionFilter.Everything, default, removed));

        // The collider that took the slot is a collider like any other, and its own handle still
        // ignores it.
        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(1, world.OverlapBoxAll(probe, CollisionFilter.Everything, contacts));
        Assert.Equal(reused, contacts[0].Target.Collider);
        Assert.Equal(0, world.OverlapBoxAll(probe, CollisionFilter.Everything, contacts, reused));
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
        CollisionLayer item = world.Layer("item");
        ColliderHandle handle = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, item, CollisionFilter.None);
        Shape2D none = default;

        Assert.Equal(0, none.PointCount);
        Assert.Throws<ArgumentException>(() => world.Add(none, Vector2.Zero, item, CollisionFilter.None));
        Assert.Throws<ArgumentException>(() => world.SetShape(handle, none));
        Assert.Throws<ArgumentException>(
            () => world.ShapeCast(none, Vector2.Zero, new Vector2(10f, 0f), CollisionFilter.Everything, out _));
        Assert.Throws<ArgumentException>(
            () => world.OverlapAll(none, Vector2.Zero, CollisionFilter.Everything, default));
        Assert.Throws<ArgumentException>(
            () => world.Move(none, Vector2.Zero, new Vector2(10f, 0f), CollisionFilter.Everything, default));
    }
}

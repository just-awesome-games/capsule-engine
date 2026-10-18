using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

// What every query seam refuses, and the proof that a refusal leaves the world exactly as it was.
public sealed class CollisionGuardTests
{
    [Fact]
    public void AWorld_RefusesAHandleOrLayerThatCameFromAnotherWorld()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        CollisionGrid2D terrain = CollisionFixtures.Paint(first, "##");
        ColliderHandle foreign = first.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, first.Layer("item"));
        ColliderHandle own = second.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, second.Layer("item"));

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
            () => second.OverlapBoxAll(terrain.CellBox(0, 0), first.CreateFilter(CollisionFixtures.Solid), default));
        Assert.Throws<ArgumentException>(
            () => second.Add(Shape2D.Box(Vector2.Zero, new Vector2(4f, 4f)), Vector2.Zero, first.Layer("item")));
        Assert.Throws<ArgumentException>(
            () => second.Raycast(Vector2.Zero, Vector2.UnitX, 10f, CollisionFilter.Everything, out _, foreign));
        Assert.Throws<ArgumentException>(
            () => second.OverlapBoxAll(CollisionFixtures.Box(0f, 0f, 8f, 8f), CollisionFilter.Everything, default, foreign));

        // The collider it did add is untouched by any of that.
        Assert.True(second.Contains(own));
        Assert.Equal(1, second.ColliderCount);
    }

    // Two worlds hand the same bit to unrelated names. One world's layer read against another's table
    // would be a silent mismatch, whether matched or combined.
    [Fact]
    public void AFilter_RefusesToMatchOrCombineALayerFromAnotherWorld()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        CollisionLayer hazard = first.Layer("hazard");
        CollisionLayer solid = second.Layer("solid");
        CollisionFilter left = CollisionFilter.Of(hazard);
        CollisionFilter right = CollisionFilter.Of(solid);

        Assert.Equal(hazard.Index, solid.Index);
        Assert.NotEqual(left, right);
        Assert.Throws<ArgumentException>(() => left.Matches(solid));
        Assert.Throws<ArgumentException>(() => first.CreateFilter("hazard").Matches(solid));
        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(hazard, solid));
        Assert.Throws<ArgumentException>(() => left.With(solid));
        Assert.Throws<ArgumentException>(() => left.Without(solid));
        Assert.Throws<ArgumentException>(() => left | right);
        Assert.Throws<ArgumentException>(() => left & right);
    }

    // A layer no world interned is the zero value of a type that is nothing but a table index. Read as
    // agnostic, it would build a filter every world resolves to its own index-0 entry.
    [Fact]
    public void ALayerNoWorldInterned_IsRefusedByEveryFilterAndWorldSeam()
    {
        CollisionWorld2D world = new();
        CollisionLayer solid = world.Layer("solid");
        CollisionFilter filter = CollisionFilter.Of(solid);
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        ColliderHandle handle = world.Add(box, Vector2.Zero, solid);

        Assert.False(world.TryFindLayer("hazard", out CollisionLayer missing));
        Assert.Equal(world.Layer(CollisionWorld2D.DefaultLayerName).Index, missing.Index);

        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(missing));
        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(solid, missing));
        Assert.Throws<ArgumentException>(() => CollisionFilter.None.With(missing));
        Assert.Throws<ArgumentException>(() => filter.With(missing));
        Assert.Throws<ArgumentException>(() => filter.Without(missing));
        Assert.Throws<ArgumentException>(() => filter.Matches(missing));
        Assert.Throws<ArgumentException>(() => CollisionFilter.Everything.Matches(missing));
        Assert.Throws<ArgumentException>(() => world.NameOf(missing));
        Assert.Throws<ArgumentException>(() => world.Add(box, Vector2.Zero, missing));
        Assert.Throws<ArgumentException>(() => world.SetLayer(handle, missing));
        Assert.Equal(solid, world.LayerOf(handle));
    }

    // A grid handle names a grid, not one shape on one layer. Every per-collider accessor refuses it
    // rather than answering about the empty slot the grid occupies.
    [Fact]
    public void ThePerColliderAccessors_RefuseAGridHandleRatherThanDescribingItsSlot()
    {
        CollisionWorld2D world = new();
        CollisionGrid2D grid = CollisionFixtures.Paint(world, "....", "####");
        ColliderHandle handle = grid.Handle;

        Assert.Throws<ArgumentException>(() => world.SetLayer(handle, world.Layer("wall")));
        Assert.Throws<ArgumentException>(() => world.PositionOf(handle));
        Assert.Throws<ArgumentException>(() => world.ShapeOf(handle));
        Assert.Throws<ArgumentException>(() => world.LayerOf(handle));
        Assert.Null(world.UserDataOf(handle));

        // The members that are about grids still take it, and the grid still answers as before.
        Assert.True(world.Contains(handle));
        Assert.Same(grid, world.GridOf(handle));
        Assert.Equal(world.Layer(CollisionFixtures.Solid), grid.LayerOf(0, 1));

        // And the refused SetLayer changed nothing a tile query reads.
        Assert.True(world.Raycast(new Vector2(8f, 0f), Vector2.UnitY, 64f, world.CreateFilter(CollisionFixtures.Solid), out RayHit2D hit));
        Assert.Equal((0, 1), (hit.Target.CellX, hit.Target.CellY));
    }

    [Fact]
    public void APairOverlap_RefusesAColliderFromAnotherWorldAndFindsNothingInOneOutsideEveryWorld()
    {
        Scene scene = new Scene().Started();
        Prober prober = new(Vector2.Zero);
        scene.Add(prober);

        Scene elsewhere = new Scene().Started();
        Prober foreign = new(Vector2.Zero);
        elsewhere.Add(foreign);

        Assert.Throws<ArgumentException>(() => prober.Collider.Overlaps(foreign.Collider));
        Assert.Throws<ArgumentNullException>(() => prober.Collider.Overlaps(null!));

        // A collider in no scene is in no world, and there is nothing there for it to touch.
        Prober unregistered = new(Vector2.Zero);
        Assert.False(prober.Collider.Overlaps(unregistered.Collider));
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
        ColliderHandle handle = second.Add(box, Vector2.Zero, item);
        Aabb2D probe = CollisionFixtures.Box(0f, 0f, 8f, 8f);

        Assert.Throws<ArgumentException>(() => second.Raycast(Vector2.Zero, Vector2.UnitX, 10f, foreign, out _));
        Assert.Throws<ArgumentException>(() => second.RaycastAll(Vector2.Zero, Vector2.UnitX, 10f, foreign, default));
        Assert.Throws<ArgumentException>(() => second.ShapeCast(box, Vector2.Zero, new Vector2(10f, 0f), foreign, out _));
        Assert.Throws<ArgumentException>(() => second.OverlapAll(box, Vector2.Zero, foreign, default));
        Assert.Throws<ArgumentException>(() => second.OverlapBoxAll(probe, foreign, default));
        Assert.Throws<ArgumentException>(() => second.OverlapColliderAll(handle, foreign, default));
        Assert.Throws<ArgumentException>(() => second.Move(box, Vector2.Zero, new Vector2(10f, 0f), foreign, default));
        Assert.Throws<ArgumentException>(() => second.MoveBox(probe, new Vector2(10f, 0f), foreign, default));
    }

    // One NaN bound does not stay in its own proxy: the tree unions boxes as it balances. It would
    // spread to ancestors shared with unrelated colliders.
    [Fact]
    public void EveryTransformSeam_RefusesANonFiniteValueAndLeavesTheWorldQueryable()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        ColliderHandle bystander = world.Add(box, new Vector2(100f, 0f), item);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Add(box, new Vector2(float.NaN, 0f), item));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => world.Add(box, new Vector2(0f, float.PositiveInfinity), item));
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
        ColliderHandle far = world.Add(wide, new Vector2(-2e38f, 0f), item);
        ColliderHandle bystander = world.Add(box, new Vector2(100f, 0f), item);

        // Both positions are finite. The displacement between them is not.
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

    // A removed collider's handle carries an index since handed to somebody else. Honouring it as
    // an ignore would silence an unrelated collider.
    [Fact]
    public void EveryQueryVerb_RefusesAStaleIgnoreRatherThanSuppressingWhateverTookItsSlot()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        Aabb2D probe = CollisionFixtures.Box(0f, 0f, 8f, 8f);

        ColliderHandle removed = world.Add(box, Vector2.Zero, item);
        world.Remove(removed);

        ColliderHandle reused = world.Add(box, Vector2.Zero, item);
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
    public void EveryShapeSeam_RefusesADefaultShapeRatherThanActingOnAnEmptyPointSet()
    {
        CollisionWorld2D world = new();
        CollisionLayer item = world.Layer("item");
        ColliderHandle handle = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, item);
        Shape2D none = default;

        Assert.Equal(0, none.PointCount);
        Assert.Throws<ArgumentException>(() => world.Add(none, Vector2.Zero, item));
        Assert.Throws<ArgumentException>(() => world.SetShape(handle, none));
        Assert.Throws<ArgumentException>(
            () => world.ShapeCast(none, Vector2.Zero, new Vector2(10f, 0f), CollisionFilter.Everything, out _));
        Assert.Throws<ArgumentException>(
            () => world.OverlapAll(none, Vector2.Zero, CollisionFilter.Everything, default));
        Assert.Throws<ArgumentException>(
            () => world.Move(none, Vector2.Zero, new Vector2(10f, 0f), CollisionFilter.Everything, default));
    }
}

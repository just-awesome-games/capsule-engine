using System.Globalization;
using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

public sealed class CollisionFilterTests
{
    [Fact]
    public void Layer_InternsTheSameNameOnceAndKeepsTheDefaultEntryFirst()
    {
        CollisionWorld2D world = new();

        CollisionLayer solid = world.Layer("solid");

        Assert.Equal(0, world.Layer(CollisionWorld2D.DefaultLayerName).Index);
        Assert.Equal(1, solid.Index);
        Assert.Equal(solid, world.Layer("solid"));
        Assert.Equal(2, world.LayerCount);
    }

    [Fact]
    public void Layer_RefusesToInternMoreThanTheWorldsCap()
    {
        CollisionWorld2D world = new();
        for (int index = 1; index < CollisionWorld2D.MaxLayers; index++)
        {
            world.Layer(index.ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(CollisionWorld2D.MaxLayers, world.LayerCount);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => world.Layer("one too many"));
        Assert.Contains($"its {CollisionWorld2D.MaxLayers} layers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Layer_AtTheLastIndexStillMatchesItsOwnFilter()
    {
        CollisionWorld2D world = new();
        CollisionLayer last = default;
        for (int index = 1; index < CollisionWorld2D.MaxLayers; index++)
        {
            last = world.Layer(index.ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(CollisionWorld2D.MaxLayers - 1, last.Index);
        Assert.True(CollisionFilter.Of(last).Matches(last));
        Assert.False(CollisionFilter.Of(last).Matches(world.Layer(CollisionWorld2D.DefaultLayerName)));
    }

    [Fact]
    public void Filter_MatchesEveryNamedLayerAndNothingElse()
    {
        CollisionWorld2D world = new();
        world.Layer("solid");
        world.Layer("platform");
        world.Layer("hazard");
        CollisionFilter filter = world.CreateFilter("solid", "platform");

        Assert.True(filter.Matches(world.Layer("solid")));
        Assert.True(filter.Matches(world.Layer("platform")));
        Assert.False(filter.Matches(world.Layer("hazard")));
        Assert.True(CollisionFilter.Everything.Matches(world.Layer("hazard")));
        Assert.True(CollisionFilter.None.IsEmpty);

        // A lookup that finds nothing interns nothing.
        Assert.False(world.TryFindLayer("climb", out _));
        Assert.Equal(4, world.LayerCount);

        CollisionFilter narrowed = filter.With(world.Layer("hazard")).Without(world.Layer("solid"));
        Assert.False(narrowed.Matches(world.Layer("solid")));
        Assert.True(narrowed.Matches(world.Layer("hazard")));
    }

    // A layer no world interned is the zero value of a type that is nothing but a table index; read
    // as agnostic it would build a filter every world resolves to its own index-0 entry.
    [Fact]
    public void ALayerNoWorldInterned_CannotBeTurnedIntoAFilterOrTestedAgainstOne()
    {
        CollisionWorld2D world = new();
        CollisionLayer solid = world.Layer("solid");
        CollisionFilter filter = CollisionFilter.Of(solid);

        Assert.False(world.TryFindLayer("hazard", out CollisionLayer missing));
        Assert.Equal(world.Layer(CollisionWorld2D.DefaultLayerName).Index, missing.Index);

        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(missing));
        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(solid, missing));
        Assert.Throws<ArgumentException>(() => CollisionFilter.None.With(missing));
        Assert.Throws<ArgumentException>(() => filter.With(missing));
        Assert.Throws<ArgumentException>(() => filter.Without(missing));
        Assert.Throws<ArgumentException>(() => filter.Matches(missing));
        Assert.Throws<ArgumentException>(() => CollisionFilter.Everything.Matches(missing));
    }

    [Fact]
    public void EveryWorldSeamTakingALayer_RefusesOneNoWorldInterned()
    {
        CollisionWorld2D world = new();
        CollisionLayer solid = world.Layer("solid");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        ColliderHandle handle = world.Add(box, Vector2.Zero, solid);

        Assert.False(world.TryFindLayer("hazard", out CollisionLayer missing));

        Assert.Throws<ArgumentException>(() => world.NameOf(missing));
        Assert.Throws<ArgumentException>(() => world.Add(box, Vector2.Zero, missing));
        Assert.Throws<ArgumentException>(() => world.SetLayer(handle, missing));
        Assert.Equal(solid, world.LayerOf(handle));
    }

    // Two worlds hand the same bit to unrelated names, so one world's mask read against another's
    // table is a silent mismatch.
    [Fact]
    public void AFilter_RefusesALayerFromAnotherWorldRatherThanMatchingItsBit()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        CollisionLayer hazard = first.Layer("hazard");
        CollisionLayer solid = second.Layer("solid");

        Assert.Equal(hazard.Index, solid.Index);
        Assert.NotEqual(CollisionFilter.Of(hazard), CollisionFilter.Of(solid));
        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(hazard).Matches(solid));
        Assert.Throws<ArgumentException>(() => first.CreateFilter("hazard").Matches(solid));
    }

    [Fact]
    public void AFilter_RefusesToCombineTwoWorldsLayers()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        CollisionLayer hazard = first.Layer("hazard");
        CollisionLayer solid = second.Layer("solid");
        CollisionFilter left = CollisionFilter.Of(hazard);
        CollisionFilter right = CollisionFilter.Of(solid);

        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(hazard, solid));
        Assert.Throws<ArgumentException>(() => left.With(solid));
        Assert.Throws<ArgumentException>(() => left.Without(solid));
        Assert.Throws<ArgumentException>(() => left | right);
        Assert.Throws<ArgumentException>(() => left & right);
    }

    [Fact]
    public void NoneAndEverything_NameNoTableAndAreTakenByEveryWorld()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        CollisionLayer hazard = first.Layer("hazard");

        Assert.True(CollisionFilter.Everything.Matches(hazard));
        Assert.True(CollisionFilter.Everything.Matches(second.Layer("solid")));
        Assert.True(CollisionFilter.Everything.Matches(first.Layer("interned afterwards")));
        Assert.False(CollisionFilter.None.Matches(hazard));

        Aabb2D probe = Aabb2D.FromCorner(Vector2.Zero, new Vector2(8f, 8f));
        Assert.Equal(0, second.OverlapBoxAll(probe, CollisionFilter.Everything, default));
        Assert.Equal(0, second.OverlapBoxAll(probe, CollisionFilter.None, default));
        Assert.Throws<ArgumentException>(() => second.OverlapBoxAll(probe, CollisionFilter.Of(hazard), default));
    }

    // A filter names layers that already exist, so a misspelled one is a typo rather than a new layer
    // that quietly matches nothing.
    [Fact]
    public void CreateFilter_RefusesALayerNameNothingHasDeclared()
    {
        CollisionWorld2D world = new();
        CollisionLayer solid = world.Layer("solid");

        Assert.Equal(solid, world.FindLayer("solid"));
        Assert.True(world.CreateFilter("solid").Matches(solid));

        ArgumentException error = Assert.Throws<ArgumentException>(() => world.CreateFilter("solid", "soild"));
        Assert.Contains("soild", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => world.FindLayer("soild"));

        // The typo interned nothing, so the world still holds the default layer and solid alone.
        Assert.Equal(2, world.LayerCount);
    }

    // A query whose filter reaches no layer any registered collider stands on skips the tree walk
    // entirely, so the set of occupied layers must track every way a collider joins, is relayered, or
    // leaves: a stale one is a collider the broadphase silently stops reporting.
    [Fact]
    public void AQuery_FindsAColliderOnWhicheverLayerItCurrentlyStandsOn()
    {
        CollisionWorld2D world = new();
        CollisionLayer wall = world.Layer("wall");
        CollisionLayer ghost = world.Layer("ghost");
        Shape2D box = Shape2D.Box(new Vector2(40f, -8f), new Vector2(8f, 16f));
        ColliderHandle handle = world.Add(box, Vector2.Zero, wall);

        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Of(wall), out _));
        Assert.False(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Of(ghost), out _));

        world.SetLayer(handle, ghost);

        Assert.False(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Of(wall), out _));
        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Of(ghost), out _));
        Assert.Equal(
            1,
            world.OverlapBoxAll(Aabb2D.FromCorner(new Vector2(40f, -8f), new Vector2(8f, 16f)), CollisionFilter.Of(ghost), new Contact2D[4]));

        world.Remove(handle);
        Assert.False(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Everything, out _));

        handle = world.Add(box, Vector2.Zero, wall);
        Assert.True(world.Raycast(Vector2.Zero, Vector2.UnitX, 200f, CollisionFilter.Of(wall), out _));

        // A move is observed by the very next query, with no step in between.
        world.SetPosition(handle, new Vector2(60f, 0f));
        Assert.Equal(
            1,
            world.OverlapBoxAll(Aabb2D.FromCorner(new Vector2(100f, -8f), new Vector2(8f, 16f)), CollisionFilter.Of(wall), new Contact2D[4]));
    }
}

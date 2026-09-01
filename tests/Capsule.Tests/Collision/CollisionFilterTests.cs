using System.Globalization;
using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

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
    public void Layer_IsDeterministicAcrossWorldsRegisteredInTheSameOrder()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();

        foreach (string name in new[] { "solid", "platform", "hazard" })
        {
            Assert.Equal(first.Layer(name).Index, second.Layer(name).Index);
        }
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
        Assert.Contains("at most 64 layers", error.Message, StringComparison.Ordinal);
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
        CollisionFilter filter = world.Filter("solid", "platform");

        Assert.True(filter.Matches(world.Layer("solid")));
        Assert.True(filter.Matches(world.Layer("platform")));
        Assert.False(filter.Matches(world.Layer("hazard")));
        Assert.True(CollisionFilter.Everything.Matches(world.Layer("hazard")));
        Assert.True(CollisionFilter.None.IsEmpty);
    }

    [Fact]
    public void TryFindLayer_DoesNotInternAName()
    {
        CollisionWorld2D world = new();

        Assert.False(world.TryFindLayer("solid", out _));
        Assert.Equal(1, world.LayerCount);
    }

    // A layer no world interned is the zero value of a type that is nothing but a table index.
    // Read as world-agnostic it would build a filter every world accepts and every world resolves
    // to its own index-0 entry, so it is refused everywhere a layer is taken instead.
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
        ColliderHandle handle = world.Add(box, Vector2.Zero, solid, CollisionFilter.None);

        Assert.False(world.TryFindLayer("hazard", out CollisionLayer missing));

        Assert.Throws<ArgumentException>(() => world.NameOf(missing));
        Assert.Throws<ArgumentException>(() => world.Add(box, Vector2.Zero, missing, CollisionFilter.None));
        Assert.Throws<ArgumentException>(() => world.SetFilter(handle, missing, CollisionFilter.None));
        Assert.Equal(solid, world.LayerOf(handle));
    }

    [Fact]
    public void WithAndWithout_AddAndRemoveOneLayer()
    {
        CollisionWorld2D world = new();
        CollisionLayer solid = world.Layer("solid");
        CollisionLayer hazard = world.Layer("hazard");

        CollisionFilter filter = CollisionFilter.None.With(solid).With(hazard).Without(solid);

        Assert.False(filter.Matches(solid));
        Assert.True(filter.Matches(hazard));
    }

    // Two worlds hand the same bit to unrelated names, which is exactly what a filter must not
    // paper over: one world's mask read against another's table is a silent mismatch.
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
        Assert.Throws<ArgumentException>(() => first.Filter("hazard").Matches(solid));
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
        Assert.Throws<ArgumentException>(() => left.Union(right));
        Assert.Throws<ArgumentException>(() => left.Intersect(right));
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
        Assert.Equal(0, second.OverlapBox(probe, CollisionFilter.Everything, default));
        Assert.Equal(0, second.OverlapBox(probe, CollisionFilter.None, default));
        Assert.Throws<ArgumentException>(() => second.OverlapBox(probe, CollisionFilter.Of(hazard), default));
    }
}

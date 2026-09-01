using System.Globalization;
using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

public sealed class CollisionFilterTests
{
    [Fact]
    public void Tag_InternsTheSameNameOnceAndKeepsTheUntaggedEntryFirst()
    {
        CollisionWorld2D world = new();

        CollisionTag solid = world.Tag("solid");

        Assert.Equal(0, world.Tag(CollisionWorld2D.UntaggedName).Index);
        Assert.Equal(1, solid.Index);
        Assert.Equal(solid, world.Tag("solid"));
        Assert.Equal(2, world.TagCount);
    }

    [Fact]
    public void Tag_IsDeterministicAcrossWorldsRegisteredInTheSameOrder()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();

        foreach (string name in new[] { "solid", "one-way", "hazard" })
        {
            Assert.Equal(first.Tag(name).Index, second.Tag(name).Index);
        }
    }

    [Fact]
    public void Tag_RefusesToInternMoreThanTheWorldsCap()
    {
        CollisionWorld2D world = new();
        for (int index = 1; index < CollisionWorld2D.MaxTags; index++)
        {
            world.Tag(index.ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(CollisionWorld2D.MaxTags, world.TagCount);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => world.Tag("one too many"));
        Assert.Contains("at most 64 tags", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_AtTheLastIndexStillMatchesItsOwnFilter()
    {
        CollisionWorld2D world = new();
        CollisionTag last = default;
        for (int index = 1; index < CollisionWorld2D.MaxTags; index++)
        {
            last = world.Tag(index.ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(CollisionWorld2D.MaxTags - 1, last.Index);
        Assert.True(CollisionFilter.Of(last).Matches(last));
        Assert.False(CollisionFilter.Of(last).Matches(world.Tag(CollisionWorld2D.UntaggedName)));
    }

    [Fact]
    public void Filter_MatchesEveryNamedTagAndNothingElse()
    {
        CollisionWorld2D world = new();
        CollisionFilter filter = world.Filter("solid", "one-way");

        Assert.True(filter.Matches(world.Tag("solid")));
        Assert.True(filter.Matches(world.Tag("one-way")));
        Assert.False(filter.Matches(world.Tag("hazard")));
        Assert.True(CollisionFilter.Everything.Matches(world.Tag("hazard")));
        Assert.True(CollisionFilter.None.IsEmpty);
    }

    [Fact]
    public void TryFindTag_DoesNotInternAName()
    {
        CollisionWorld2D world = new();

        Assert.False(world.TryFindTag("solid", out _));
        Assert.Equal(1, world.TagCount);
    }

    // A tag no world interned is the zero value of a type that is nothing but a table index. Read
    // as world-agnostic it would build a filter every world accepts and every world resolves to
    // its own index-0 entry, so it is refused everywhere a tag is taken instead.
    [Fact]
    public void ATagNoWorldInterned_CannotBeTurnedIntoAFilterOrTestedAgainstOne()
    {
        CollisionWorld2D world = new();
        CollisionTag solid = world.Tag("solid");
        CollisionFilter filter = CollisionFilter.Of(solid);

        Assert.False(world.TryFindTag("hazard", out CollisionTag missing));
        Assert.Equal(world.Tag(CollisionWorld2D.UntaggedName).Index, missing.Index);

        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(missing));
        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(solid, missing));
        Assert.Throws<ArgumentException>(() => CollisionFilter.None.With(missing));
        Assert.Throws<ArgumentException>(() => filter.With(missing));
        Assert.Throws<ArgumentException>(() => filter.Without(missing));
        Assert.Throws<ArgumentException>(() => filter.Matches(missing));
        Assert.Throws<ArgumentException>(() => CollisionFilter.Everything.Matches(missing));
    }

    [Fact]
    public void EveryWorldSeamTakingATag_RefusesOneNoWorldInterned()
    {
        CollisionWorld2D world = new();
        CollisionTag solid = world.Tag("solid");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        ColliderHandle handle = world.Add(box, Vector2.Zero, solid, CollisionFilter.None);

        Assert.False(world.TryFindTag("hazard", out CollisionTag missing));

        Assert.Throws<ArgumentException>(() => world.NameOf(missing));
        Assert.Throws<ArgumentException>(() => world.Add(box, Vector2.Zero, missing, CollisionFilter.None));
        Assert.Throws<ArgumentException>(() => world.SetFilter(handle, missing, CollisionFilter.None));
        Assert.Equal(solid, world.TagOf(handle));
    }

    [Fact]
    public void WithAndWithout_AddAndRemoveOneTag()
    {
        CollisionWorld2D world = new();
        CollisionTag solid = world.Tag("solid");
        CollisionTag hazard = world.Tag("hazard");

        CollisionFilter filter = CollisionFilter.None.With(solid).With(hazard).Without(solid);

        Assert.False(filter.Matches(solid));
        Assert.True(filter.Matches(hazard));
    }

    // Two worlds hand the same bit to unrelated names, which is exactly what a filter must not
    // paper over: one world's mask read against another's table is a silent mismatch.
    [Fact]
    public void AFilter_RefusesATagFromAnotherWorldRatherThanMatchingItsBit()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        CollisionTag hazard = first.Tag("hazard");
        CollisionTag solid = second.Tag("solid");

        Assert.Equal(hazard.Index, solid.Index);
        Assert.NotEqual(CollisionFilter.Of(hazard), CollisionFilter.Of(solid));
        Assert.Throws<ArgumentException>(() => CollisionFilter.Of(hazard).Matches(solid));
        Assert.Throws<ArgumentException>(() => first.Filter("hazard").Matches(solid));
    }

    [Fact]
    public void AFilter_RefusesToCombineTwoWorldsTags()
    {
        CollisionWorld2D first = new();
        CollisionWorld2D second = new();
        CollisionTag hazard = first.Tag("hazard");
        CollisionTag solid = second.Tag("solid");
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
        CollisionTag hazard = first.Tag("hazard");

        Assert.True(CollisionFilter.Everything.Matches(hazard));
        Assert.True(CollisionFilter.Everything.Matches(second.Tag("solid")));
        Assert.True(CollisionFilter.Everything.Matches(first.Tag("interned afterwards")));
        Assert.False(CollisionFilter.None.Matches(hazard));

        Aabb2D probe = Aabb2D.FromCorner(Vector2.Zero, new Vector2(8f, 8f));
        Assert.Equal(0, second.OverlapBox(probe, CollisionFilter.Everything, default));
        Assert.Equal(0, second.OverlapBox(probe, CollisionFilter.None, default));
        Assert.Throws<ArgumentException>(() => second.OverlapBox(probe, CollisionFilter.Of(hazard), default));
    }
}

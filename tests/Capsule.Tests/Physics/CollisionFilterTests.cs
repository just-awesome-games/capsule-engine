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
    public void Layer_FillsEveryIndexUpToTheWorldsCapAndRefusesOneMore()
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

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => world.Layer("one too many"));
        Assert.Contains($"its {CollisionWorld2D.MaxLayers} layers", error.Message, StringComparison.Ordinal);
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

    // A filter names layers that already exist. A misspelled one is a typo rather than a new layer
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

        // The typo interned nothing. The world still holds the default layer and solid alone.
        Assert.Equal(2, world.LayerCount);
    }

    // A query whose filter reaches no layer any registered collider stands on skips the tree walk
    // entirely. The set of occupied layers must track every way a collider joins, is relayered, or
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

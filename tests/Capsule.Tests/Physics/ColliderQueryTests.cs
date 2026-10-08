using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Physics;

/// <summary>The queries a collider answers about the world from where it stands.</summary>
public sealed class ColliderQueryTests
{
    // Three rows of 16-unit cells with a solid floor across the third: its top face is y = 32, and
    // a box collider is corner-anchored, so a ray leaves a prober from four units below its corner.
    private const float FloorTop = 32f;

    [Fact]
    public void AColliderRay_HonoursItsOwnFilterAndTheOneAGivenCallNames()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####").Started();
        Prober prober = new(new Vector2(24f, 8f));
        scene.Add(prober);

        Assert.Equal(CollisionFilter.None, prober.Collider.Filter);
        Assert.False(prober.Collider.Raycast(Vector2.UnitY, 40f, out _));

        Assert.True(prober.Collider.Raycast(Vector2.UnitY, 40f, scene.Collision.CreateFilter("solid"), out RayHit2D named));
        Assert.Equal(FloorTop - prober.Collider.Bounds.Center.Y, named.Distance);
        Assert.Equal(new Vector2(0f, -1f), named.Normal);

        prober.Collider.Detects = new("solid");

        Assert.True(prober.Collider.Raycast(Vector2.UnitY, 40f, out RayHit2D own));
        Assert.Equal(named, own);
    }

    // The ray starts inside the caster's own shape, so a query that did not exclude it would answer
    // with the caster at distance 0, and win the tie by holding the older slot.
    [Fact]
    public void AColliderRay_PassesThroughTheColliderItStartsFrom()
    {
        Scene scene = new Scene().Started();
        Prober caster = new(Vector2.Zero, "default");
        Prober target = new(Vector2.Zero);
        scene.Add(caster);
        scene.Add(target);

        Assert.True(caster.Collider.Raycast(Vector2.UnitY, 40f, out RayHit2D hit));
        Assert.Equal(target.Collider.Handle, hit.Target.Collider);
    }

    [Fact]
    public void AColliderRay_ReportsTheNearestOfATileAndACollider()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####").Started();
        Prober prober = new(new Vector2(24f, 8f), "solid");
        Prober blocker = new(new Vector2(24f, 20f));
        blocker.Collider.Layer = "solid";
        scene.Add(prober);
        scene.Add(blocker);

        Assert.True(prober.Collider.Raycast(Vector2.UnitY, 40f, out RayHit2D nearer));
        Assert.Equal(blocker.Collider.Handle, nearer.Target.Collider);
        Assert.Equal(8f, nearer.Distance);

        // Behind the floor now, so the tile is what the ray reaches first.
        blocker.Position = new Vector2(24f, 44f);

        Assert.True(prober.Collider.Raycast(Vector2.UnitY, 40f, out RayHit2D farther));
        Assert.True(farther.Target.IsGridCell);
        Assert.Equal(FloorTop - prober.Collider.Bounds.Center.Y, farther.Distance);
    }

    // The world allows a zero distance. A collider ignoring itself would always miss, so it refuses one.
    [Fact]
    public void AColliderRay_RefusesAZeroDistance()
    {
        Scene scene = new();
        Prober prober = new(Vector2.Zero);
        scene.Add(prober);

        Assert.Throws<ArgumentOutOfRangeException>(() => prober.Collider.Raycast(Vector2.UnitY, 0f, out _));
    }

    [Fact]
    public void AColliderRay_NeedsAWorldToCastInto()
    {
        Prober prober = new(Vector2.Zero);

        Assert.Throws<InvalidOperationException>(() => prober.Collider.Raycast(Vector2.UnitY, 40f, out _));
    }

    [Fact]
    public void APairOverlap_IgnoresBothCollidersFilters()
    {
        Scene scene = new Scene().Started();
        Prober first = new(Vector2.Zero);
        Prober second = new(new Vector2(4f, 0f));
        scene.Add(first);
        scene.Add(second);

        // Neither detects anything, so neither would find the other through a filtered query.
        Assert.Equal(CollisionFilter.None, first.Collider.Filter);
        Assert.Equal(0, first.Collider.OverlapAll(new Contact2D[4]));

        Assert.True(first.Collider.Overlaps(second.Collider));
        Assert.True(second.Collider.Overlaps(first.Collider));
    }

    [Fact]
    public void APairOverlap_IsFalseForCollidersApartAndForOneAgainstItself()
    {
        Scene scene = new Scene().Started();
        Prober first = new(Vector2.Zero);
        Prober second = new(new Vector2(40f, 0f));
        scene.Add(first);
        scene.Add(second);

        Assert.False(first.Collider.Overlaps(second.Collider));
        Assert.False(first.Collider.Overlaps(first.Collider, out Contact2D none));
        Assert.Equal(default, none);
    }

    [Fact]
    public void APairOverlap_DescribesTheContactAnOverlapQueryWouldReportForTheSamePair()
    {
        Scene scene = new Scene().Started();
        Prober first = new(Vector2.Zero, "default");
        Prober second = new(new Vector2(4f, 0f));
        scene.Add(first);
        scene.Add(second);

        Contact2D[] contacts = new Contact2D[4];
        Assert.Equal(1, first.Collider.OverlapAll(contacts));

        Assert.True(first.Collider.Overlaps(second.Collider, out Contact2D pair));
        Assert.Equal(contacts[0], pair);
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

        // In no scene, so in no world's terms: there is nothing there to be touched.
        Prober unregistered = new(Vector2.Zero);
        Assert.False(prober.Collider.Overlaps(unregistered.Collider));
    }

    [Fact]
    public void ColliderOf_ResolvesAHitToItsColliderAndEntity_AndATileOrARemovedColliderToNull()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####").Started();
        Prober probe = new(new Vector2(20f, 28f), "solid", "default");
        Prober other = new(new Vector2(24f, 28f));
        scene.Add(probe);
        scene.Add(other);

        Span<Contact2D> found = stackalloc Contact2D[8];
        int count = probe.Collider.OverlapAll(found);

        Contact2D tile = found[0];
        Contact2D collider = found[count - 1];
        Assert.True(tile.Target.IsGridCell);
        Assert.Null(scene.ColliderOf(tile.Target.Collider));
        Assert.Same(other.Collider, scene.ColliderOf(collider.Target.Collider));
        Assert.Same(other, scene.ColliderOf(collider.Target.Collider)!.Entity);

        scene.Remove(other);

        Assert.Null(scene.ColliderOf(collider.Target.Collider));
        Assert.Null(scene.ColliderOf(ColliderHandle.None));
    }

    [Fact]
    public void TileOf_ResolvesAHitToItsMapAndCell_AndAColliderOrARemovedMapToNull()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####").Started();
        TileMap map = SceneFixtures.TerrainOf(scene);
        Prober prober = new(new Vector2(24f, 8f), "solid");
        scene.Add(prober);

        Assert.True(prober.Collider.Raycast(Vector2.UnitY, 40f, out RayHit2D floor));
        TileContact2D tile = Assert.NotNull(scene.TileOf(floor.Target));
        Assert.Same(map, tile.Map);
        Assert.Equal((1, 2), (tile.X, tile.Y));
        Assert.Same(map.TileAt(1, 2), tile.Type);

        Prober blocker = new(new Vector2(24f, 20f));
        blocker.Collider.Layer = "solid";
        scene.Add(blocker);

        Assert.True(prober.Collider.Raycast(Vector2.UnitY, 40f, out RayHit2D collider));
        Assert.Null(scene.TileOf(collider.Target));

        scene.Remove(map);

        Assert.Null(scene.TileOf(floor.Target));
    }
}

using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

using Body = Capsule.Tests.Scenes.SceneFixtures.Body;

namespace Capsule.Tests.Physics;

public sealed class ColliderValidationTests
{
    // A rejected set must leave the component, its entity and the world identical — not commit the
    // field and then fail on the way to the broadphase.
    [Fact]
    public void ARejectedSizeOrOffsetSet_LeavesTheColliderAndItsProxyExactlyAsTheyWere()
    {
        Scene scene = new();
        Body body = new(new Vector2(10f, 10f));
        scene.Add(body);

        Shape2D original = body.Collider.Shape;
        Aabb2D bounds = body.Collider.Bounds;

        Assert.Throws<ArgumentException>(() => body.Collider.Size = new Vector2(0f, 8f));
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Collider.Size = new Vector2(-8f, 8f));
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Collider.Offset = new Vector2(float.NaN, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Collider.Offset = new Vector2(0f, float.PositiveInfinity));

        Assert.Equal(new Vector2(8f, 8f), body.Collider.Size);
        Assert.Equal(original, body.Collider.Shape);
        Assert.Equal(Vector2.Zero, body.Collider.Offset);
        Assert.Equal(bounds, body.Collider.Bounds);
        Assert.Equal(original, scene.Collision.ShapeOf(body.Collider.Handle));
        Assert.Equal(new Vector2(10f, 10f), scene.Collision.PositionOf(body.Collider.Handle));

        // Still exactly where the proxy said it was.
        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(1, scene.Collision.OverlapBoxAll(bounds, CollisionFilter.Everything, contacts));
    }

    // The typed setters are the only way a shape changes now, and a registered collider has to be
    // queried as its new shape from the moment one returns.
    [Fact]
    public void ATypedSetterOnARegisteredCollider_ResyncsTheShapeTheWorldQueriesBy()
    {
        Scene scene = new();
        Body body = new(new Vector2(100f, 100f));
        scene.Add(body);

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Aabb2D reach = Aabb2D.FromCorner(new Vector2(120f, 100f), new Vector2(8f, 8f));
        Assert.Equal(0, scene.Collision.OverlapBoxAll(reach, CollisionFilter.Everything, contacts));

        body.Collider.Size = new Vector2(64f, 8f);

        Assert.Equal(new Vector2(64f, 8f), body.Collider.Size);
        Assert.Equal(1, scene.Collision.OverlapBoxAll(reach, CollisionFilter.Everything, contacts));
        Assert.Equal(body.Collider.Handle, contacts[0].Target.Collider);
    }

    // The offset half of the check is world-independent, so it fires where the mistake was made
    // rather than surfacing later as a failure to join a scene.
    [Fact]
    public void AnUnplaceableOffsetOnADetachedCollider_IsRefusedAtSetTimeNotAtAttachTime()
    {
        BoxCollider2D collider = new(new Vector2(8f, 8f));

        Assert.Throws<ArgumentOutOfRangeException>(() => collider.Offset = new Vector2(float.PositiveInfinity, 0f));
        Assert.Equal(Vector2.Zero, collider.Offset);

        // Finite, and still no shape: the offset carries this one's bounds off the end of the range.
        CircleCollider2D wide = new(8e37f);
        Assert.Throws<ArgumentException>(() => wide.Offset = new Vector2(3e38f, 0f));
        Assert.Equal(Vector2.Zero, wide.Offset);

        Scene scene = new();
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        scene.Add(drifter);
        drifter.Add(collider);

        Assert.Same(scene.Collision, collider.World);
    }

    [Fact]
    public void ARejectedDetectsCall_LeavesTheColliderFilteringAsItDid()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.SetFilter("solid");
        body.Mover.BlocksOn("solid");
        scene.Add(body);

        CollisionFilter before = body.Collider.Filter;

        // The bad name is second, so a call that committed as it went would already have thrown the
        // old list away and kept the first.
        Assert.Throws<ArgumentException>(() => body.Collider.SetFilter("wall", " "));

        Assert.Equal(before, body.Collider.Filter);

        // The stored names are what the next scene rebuilds the filter from, which is the only
        // place a half-applied list would ever show itself.
        scene.Remove(body);
        Scene second = SceneFixtures.Terrain("....", "####");
        second.Add(body);

        Assert.True(body.Collider.Filter.Matches(second.Collision.Layer("solid")));
        Assert.True(body.Mover.Move(new Vector2(0f, 60f)).Blocked);
    }

    // Layer names are interned where they are first needed, and the world's table is the whole of
    // the contract: the sixty-fifth name has nowhere to go, whoever asks for it.
    [Fact]
    public void AColliderNeedingALayerTheWorldHasNoRoomFor_IsRefusedWhereTheNameIsInterned()
    {
        Scene scene = new();
        Body host = new(Vector2.Zero);
        scene.Add(host);
        Saturate(scene.Collision);

        BoxCollider2D late = new(new Vector2(8f, 8f));
        late.SetFilter("a name this world has never seen");

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => host.Add(late));

        Assert.Contains($"{CollisionWorld2D.MaxLayers} layers", refused.Message, StringComparison.Ordinal);
        Assert.Null(late.World);
        Assert.True(late.Handle.IsNone);
        Assert.Equal(CollisionWorld2D.MaxLayers, scene.Collision.LayerCount);
    }

    [Fact]
    public void AColliderTakingTheLastLayerTheWorldHasRoomFor_Registers()
    {
        Scene scene = new();
        Body host = new(Vector2.Zero);
        scene.Add(host);

        // One short of the cap, so the collider's own layer is the last name that fits.
        Saturate(scene.Collision, spare: 1);

        BoxCollider2D last = new(new Vector2(8f, 8f)) { Layer = "the last one that fits" };
        host.Add(last);

        Assert.Same(scene.Collision, last.World);
        Assert.True(scene.Collision.Contains(last.Handle));
        Assert.Equal(CollisionWorld2D.MaxLayers, scene.Collision.LayerCount);
    }

    private static void Saturate(CollisionWorld2D world, int spare = 0)
    {
        for (int index = world.LayerCount; index < CollisionWorld2D.MaxLayers - spare; index++)
        {
            world.Layer($"filler-{index}");
        }
    }

    // Every typed collider validates through the shape factory it builds with, at construction and
    // at every set, so a shape the queries would refuse never reaches one.
    [Fact]
    public void ATypedCollider_RefusesAShapeItsFactoryWould()
    {
        Assert.Throws<ArgumentException>(() => new BoxCollider2D(new Vector2(0f, 8f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircleCollider2D(0f));
        Assert.Throws<ArgumentException>(() => new CapsuleCollider2D(Vector2.Zero, Vector2.Zero, 4f));
        Assert.Throws<ArgumentException>(() => new PolygonCollider2D([Vector2.Zero, Vector2.UnitX]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircleCollider2D(4f) { Radius = float.NaN });
    }

    // Setting the layer of a registered collider has to reach the world at once: a query on the very
    // next line filters by what it is on now, not by what it was on.
    [Fact]
    public void SettingTheLayerOfARegisteredCollider_ReFiltersImmediately()
    {
        Scene scene = new();
        Body body = new(Vector2.Zero);
        scene.Add(body);

        // A collider is on the default layer until it is told otherwise, which is what makes
        // things collide out of the box.
        Assert.Equal(CollisionWorld2D.DefaultLayerName, body.Collider.Layer);
        Assert.Equal(
            scene.Collision.Layer(CollisionWorld2D.DefaultLayerName),
            scene.Collision.LayerOf(body.Collider.Handle));

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Aabb2D probe = Aabb2D.FromCorner(Vector2.Zero, new Vector2(8f, 8f));
        CollisionFilter hazard = CollisionFilter.Of(scene.Collision.Layer("hazard"));

        Assert.Equal(0, scene.Collision.OverlapBoxAll(probe, hazard, contacts));

        body.Collider.Layer = "hazard";

        Assert.Equal("hazard", body.Collider.Layer);
        Assert.Equal(scene.Collision.Layer("hazard"), scene.Collision.LayerOf(body.Collider.Handle));
        Assert.Equal(1, scene.Collision.OverlapBoxAll(probe, hazard, contacts));
    }
}

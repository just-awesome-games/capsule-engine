using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

using Body = Capsule.Tests.Scenes.SceneFixtures.Body;

namespace Capsule.Tests.Physics;

public sealed class ColliderValidationTests
{
    // A rejected set must leave the component, its entity and the world identical. It must not commit
    // the field and then fail on the way to the broadphase.
    [Fact]
    public void ARejectedSizeOrOffsetSet_LeavesTheColliderAndItsProxyExactlyAsTheyWere()
    {
        Scene scene = new Scene().Started();
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
        Scene scene = new Scene().Started();
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

        Scene scene = new Scene().Started();
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        scene.Add(drifter);
        drifter.Add(collider);

        Assert.Same(scene.Collision, collider.World);
    }

    // Each setter resolves its mask before storing it. A mask the world has no room for leaves the
    // old one in place, and the next scene rebuilds from that.
    [Fact]
    public void AMaskTheWorldHasNoRoomFor_LeavesTheColliderAndBodyFilteringAsTheyDid()
    {
        Scene scene = SceneFixtures.Terrain("....", "####").Started();
        Body body = new(new Vector2(4f, 8f));
        CollisionMask solid = new("solid");
        body.Collider.Detects = solid;
        body.Mover.BlockedBy = solid;
        scene.Add(body);
        Saturate(scene.Collision);

        CollisionFilter before = body.Collider.Filter;
        CollisionMask unseen = new("a name this world has never seen");

        Assert.Throws<InvalidOperationException>(() => body.Collider.Detects = unseen);
        Assert.Throws<InvalidOperationException>(() => body.Mover.BlockedBy = unseen);

        Assert.Same(solid, body.Collider.Detects);
        Assert.Same(solid, body.Mover.BlockedBy);
        Assert.Equal(before, body.Collider.Filter);

        scene.Remove(body);
        Scene second = SceneFixtures.Terrain("....", "####").Started();
        second.Add(body);

        Assert.True(body.Collider.Filter.Matches(second.Collision.Layer("solid")));
        Assert.True(body.Mover.Move(new Vector2(0f, 60f)).Blocked);
    }

    // Layer names are interned where they are first needed, and the world's table is the whole of
    // the contract: the sixty-fifth name has nowhere to go, whoever asks for it.
    [Fact]
    public void AColliderNeedingALayerTheWorldHasNoRoomFor_IsRefusedWhereTheNameIsInterned()
    {
        Scene scene = new Scene().Started();
        Body host = new(Vector2.Zero);
        scene.Add(host);
        Saturate(scene.Collision);

        BoxCollider2D late = new(new Vector2(8f, 8f));
        late.Detects = new("a name this world has never seen");

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => host.Add(late));

        Assert.Contains($"{CollisionWorld2D.MaxLayers} layers", refused.Message, StringComparison.Ordinal);
        Assert.Null(late.World);
        Assert.True(late.Handle.IsNone);
        Assert.Equal(CollisionWorld2D.MaxLayers, scene.Collision.LayerCount);
    }

    private static void Saturate(CollisionWorld2D world)
    {
        for (int index = world.LayerCount; index < CollisionWorld2D.MaxLayers; index++)
        {
            world.Layer($"filler-{index}");
        }
    }

    // Setting the layer of a registered collider reaches the world at once.
    [Fact]
    public void SettingTheLayerOfARegisteredCollider_ReFiltersImmediately()
    {
        Scene scene = new Scene().Started();
        Body body = new(Vector2.Zero);
        scene.Add(body);

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

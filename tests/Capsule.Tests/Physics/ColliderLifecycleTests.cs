using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

using Body = Capsule.Tests.Scenes.SceneFixtures.Body;

namespace Capsule.Tests.Physics;

public sealed class ColliderLifecycleTests
{
    [Fact]
    public void ACollider_RegistersWhenItsEntityJoinsAndUnregistersWhenItLeaves()
    {
        Scene scene = new();
        Body body = new(new Vector2(10f, 10f));

        Assert.Null(body.Collider.World);
        Assert.True(body.Collider.Handle.IsNone);

        scene.Add(body);

        Assert.Same(scene.Collision, body.Collider.World);
        Assert.True(scene.Collision.Contains(body.Collider.Handle));

        ColliderHandle held = body.Collider.Handle;
        scene.Remove(body);

        Assert.Null(body.Collider.World);
        Assert.False(scene.Collision.Contains(held));
    }

    [Fact]
    public void AColliderAttachedToAnEntityAlreadyInAScene_RegistersImmediately()
    {
        Scene scene = new();
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        scene.Add(drifter);

        BoxCollider2D collider = new(new Vector2(8f, 8f));
        drifter.Add(collider);

        Assert.Same(scene.Collision, collider.World);

        drifter.Remove(collider);

        Assert.Null(collider.World);
    }

    [Fact]
    public void AColliderFollowsItsEntity_ThroughAWriteAndThroughATeleport()
    {
        Scene scene = new();
        Body body = new(Vector2.Zero);
        scene.Add(body);

        body.Position = new Vector2(40f, 0f);
        Assert.Equal(new Vector2(40f, 0f), scene.Collision.PositionOf(body.Collider.Handle));

        body.Teleport(new Vector2(-25f, 12f));
        Assert.Equal(new Vector2(-25f, 12f), scene.Collision.PositionOf(body.Collider.Handle));
    }

    [Fact]
    public void AColliderIsPlacedByItsOffsetTheWayASpriteRendererIs()
    {
        Scene scene = new();
        Body body = new(new Vector2(100f, 100f));
        body.Collider.Offset = new Vector2(-4f, -8f);
        scene.Add(body);

        Assert.Equal(new Vector2(96f, 92f), body.Collider.Bounds.Min);
        Assert.Equal(new Vector2(104f, 100f), body.Collider.Bounds.Max);
    }

    [Fact]
    public void ADisabledCollider_LeavesTheWorldAndCanBeEnabledAgain()
    {
        Scene scene = new();
        Body first = new(Vector2.Zero);
        Body second = new(new Vector2(4f, 0f));
        first.Collider.SetFilter("other");
        first.Collider.ReportsContacts = true;
        second.Collider.Layer = "other";

        int entered = 0;
        int exited = 0;
        first.Collider.ContactEntered += _ => entered++;
        first.Collider.ContactExited += _ => exited++;

        scene.Add(first);
        scene.Add(second);
        using SimulationHost run = new(scene);
        run.Step();

        ColliderHandle original = first.Collider.Handle;
        Assert.Equal(1, entered);

        first.Collider.Enabled = false;

        Assert.False(scene.Collision.Contains(original));
        Assert.Null(first.Collider.World);
        Assert.True(first.Collider.Handle.IsNone);
        Assert.Empty(first.Collider.Touching.ToArray());
        Assert.Equal(1, exited);
        Assert.Throws<InvalidOperationException>(() => first.Mover.Move(Vector2.UnitX));

        first.Collider.Enabled = true;
        run.Step();

        Assert.Same(scene.Collision, first.Collider.World);
        Assert.Equal(2, entered);
    }

    // A collider keeps layer names, not bits, so the scene it lands in is the one it filters against.
    [Fact]
    public void AColliderCarriedToAnotherScene_RebuildsItsFilterAgainstTheNewWorld()
    {
        Scene first = SceneFixtures.Terrain("....", "####");
        Body body = new(new Vector2(4f, 8f));
        body.Collider.SetFilter("solid");
        body.Collider.ReportsContacts = true;
        first.Add(body);

        CollisionFilter inFirst = body.Collider.Filter;
        first.Remove(body);

        Assert.Equal(CollisionFilter.None, body.Collider.Filter);

        // Names interned ahead of 'solid' land it on a different bit, so a filter carried over
        // would match some other tile type rather than simply matching nothing.
        Scene second = new();
        second.Collision.Layer("hazard");
        second.Collision.Layer("water");
        second.Collision.Layer("ladder");
        second.Add(new TileMap(SceneFixtures.TerrainGrid("....", "####")));
        second.Add(body);

        Assert.NotEqual(first.Collision.Layer("solid").Index, second.Collision.Layer("solid").Index);
        Assert.NotEqual(inFirst, body.Collider.Filter);
        Assert.Throws<ArgumentException>(
            () => second.Collision.OverlapBoxAll(body.Collider.Bounds, inFirst, default));

        using SceneSimulation simulation = new(second);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["solid"], body.Collider.Touching.ToArray().Select(contact => contact.LayerName));
    }
}

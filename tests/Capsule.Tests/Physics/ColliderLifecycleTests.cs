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
        first.Collider.Detects = new("other");
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
        body.Collider.Detects = new("solid");
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
        second.Add(SceneFixtures.Colliding(SceneFixtures.TerrainGrid("....", "####")));
        second.Add(body);

        Assert.NotEqual(first.Collision.Layer("solid").Index, second.Collision.Layer("solid").Index);
        Assert.NotEqual(inFirst, body.Collider.Filter);
        Assert.Throws<ArgumentException>(
            () => second.Collision.OverlapBoxAll(body.Collider.Bounds, inFirst, default));

        using SceneSimulation simulation = new(second);
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(["solid"], body.Collider.Touching.ToArray().Select(contact => contact.LayerName));
    }

    [Fact]
    public void AGameDefinedCollider_StepsAsItsEntitysOnlyComponent()
    {
        Scene scene = new();
        Holder entity = new();
        SteppingCollider collider = new();
        entity.Add(collider);
        scene.Add(entity);

        using SimulationHost run = new(scene);
        run.Step();

        Assert.Equal(1, collider.StepCount);
        Assert.Equal(1, collider.LateStepCount);
    }

    // A box scales its offset and size about the entity's position, written while in a scene or
    // parented under the scale outside one. A negative axis mirrors it and a squash stretches it. A zero
    // axis takes it out of the world, raising nothing and leaving it enabled, until the scale spans again.
    [Theory]
    [InlineData(false, -1f, 1f, -3f, 2f, -1f, 6f)]
    [InlineData(true, 2f, 0.5f, 2f, 1f, 6f, 3f)]
    [InlineData(true, 0f, 1f, float.NaN, 0f, 0f, 0f)]
    [InlineData(false, 1f, 0f, float.NaN, 0f, 0f, 0f)]
    public void ABox_FollowsAnAxisAlignedScaleInItsAncestry(
        bool writtenInScene,
        float scaleX,
        float scaleY,
        float left,
        float top,
        float right,
        float bottom)
    {
        Placed root = new(new Vector2(100f, 50f));
        Holder held = new();
        BoxCollider2D box = new(new Vector2(2f, 4f)) { Offset = new Vector2(1f, 2f) };
        held.Add(box);
        Scene scene = new();

        if (writtenInScene)
        {
            held.Parent = root;
            scene.Add(root);
            root.Scale = new Vector2(scaleX, scaleY);
        }
        else
        {
            root.Scale = new Vector2(scaleX, scaleY);
            held.Parent = root;
            scene.Add(root);
        }

        Assert.True(box.Enabled);
        Assert.Equal(new Vector2(1f, 2f), box.Offset);
        Assert.Equal(new Vector2(2f, 4f), box.Size);

        if (float.IsNaN(left))
        {
            Assert.Null(box.World);
            Assert.Equal(0, scene.Collision.ColliderCount);

            root.Scale = Vector2.One;
            Assert.Same(scene.Collision, box.World);
            Assert.Equal(new Vector2(101f, 52f), box.Bounds.Min);
            return;
        }

        Assert.Equal(new Aabb2D(new Vector2(100f + left, 50f + top), new Vector2(100f + right, 50f + bottom)), box.Bounds);
        Assert.Equal(box.Bounds, scene.Collision.WorldShapeOf(box.Handle).Bounds);
    }

    private sealed class Holder() : Entity(Vector2.Zero);

    private sealed class Placed(Vector2 position) : Entity(position);

    private sealed class SteppingCollider() : Collider2D(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)))
    {
        internal int StepCount { get; private set; }

        internal int LateStepCount { get; private set; }

        protected internal override void OnStep(in StepContext context) => StepCount++;

        protected internal override void OnLateStep(in StepContext context) => LateStepCount++;
    }
}

using System.Numerics;
using Capsule.Collision;
using Capsule.Scenes;
using Capsule.Scenes.Physics;

namespace Capsule.Tests.Scenes.Physics;

public sealed class KinematicMoverTests
{
    // One mover per entity: two would each write the entity's position from their own sweep, and
    // the second one to run would silently undo the first.
    [Fact]
    public void ASecondMoverOnOneEntity_IsRefusedWithBothItAndTheEntityUntouched()
    {
        SceneFixtures.Drifter carrier = new(Vector2.Zero);
        BoxCollider2D collider = new(new Vector2(8f, 8f));
        carrier.Add(collider);

        KinematicMover2D held = new(collider);
        carrier.Add(held);

        KinematicMover2D offered = new(collider);
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => carrier.Add(offered));

        Assert.Contains("KinematicMover2D", refused.Message, StringComparison.Ordinal);
        Assert.Null(offered.Entity);
        Assert.Same(held, carrier.Get<KinematicMover2D>());

        // The entity never took hold of it, so it has nothing to give back.
        Assert.Throws<InvalidOperationException>(() => carrier.Remove(offered));

        // And the slot is the type's, not the instance's: freeing it lets the next one in.
        carrier.Remove(held);
        carrier.Add(offered);

        Assert.Same(carrier, offered.Entity);
    }

    // The pair is judged when the entity joins a scene, not as each component is attached, so a
    // constructor may add them in either order.
    [Fact]
    public void AnEntityAddingItsMoverBeforeItsCollider_JoinsASceneAndMoves()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        MoverFirst body = new(new Vector2(8f, 8f));

        scene.Add(body);

        MoveResult2D result = body.Mover.Move(new Vector2(0f, 60f));

        Assert.Same(scene, body.Scene);
        Assert.True(result.BlockedY);
        Assert.Equal(24f, body.Position.Y, 2f * CollisionWorld2D.LinearSlop);
    }

    [Fact]
    public void AMoverWhoseColliderIsOnAnotherEntity_IsRefusedAtAdmissionWithNothingRegistered()
    {
        Scene scene = new();

        SceneFixtures.Drifter elsewhere = new(Vector2.Zero);
        BoxCollider2D borrowed = new(new Vector2(8f, 8f));
        elsewhere.Add(borrowed);

        SceneFixtures.Drifter carrier = new(Vector2.Zero);
        BoxCollider2D own = new(new Vector2(8f, 8f));
        carrier.Add(own);
        carrier.Add(new KinematicMover2D(borrowed));

        Assert.Throws<InvalidOperationException>(() => scene.Add(carrier));

        Assert.Null(carrier.Scene);
        Assert.Null(own.World);
        Assert.True(own.Handle.IsNone);
        Assert.Equal(0, scene.Collision.ColliderCount);
    }

    private sealed class MoverFirst : Entity
    {
        internal MoverFirst(Vector2 position)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(8f, 8f));
            Mover = new KinematicMover2D(Collider);
            Mover.BlocksOn("solid");

            // The mover first: the entity is judged whole when it joins a scene.
            Add(Mover);
            Add(Collider);
        }

        internal BoxCollider2D Collider { get; }

        internal KinematicMover2D Mover { get; }
    }
}

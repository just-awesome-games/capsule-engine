using System.Numerics;
using Capsule.Collision;
using Capsule.Scenes;
using Capsule.Scenes.Physics;

namespace Capsule.Tests.Scenes.Physics;

public sealed class KinematicBodyTests
{
    // One body per entity: two would each write the entity's position from their own sweep, and
    // the second one to run would silently undo the first.
    [Fact]
    public void ASecondBodyOnOneEntity_IsRefused()
    {
        SceneFixtures.Drifter carrier = new(Vector2.Zero);
        BoxCollider2D collider = new(new Vector2(8f, 8f));
        carrier.Add(collider);

        KinematicBody2D held = new(collider);
        carrier.Add(held);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => carrier.Add(new KinematicBody2D(collider)));

        Assert.Contains("KinematicBody2D", refused.Message, StringComparison.Ordinal);
        Assert.Same(held, carrier.Get<KinematicBody2D>());
    }

    // The pair is judged when the entity joins a scene, not as each component is attached, so a
    // constructor may add them in either order.
    [Fact]
    public void AnEntityAddingItsBodyBeforeItsCollider_JoinsASceneAndMoves()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        BodyFirst body = new(new Vector2(8f, 8f));

        scene.Add(body);

        MoveResult2D result = body.Body.Move(new Vector2(0f, 60f));

        Assert.Same(scene, body.Scene);
        Assert.True(result.BlockedY);
        Assert.Equal(24f, body.Position.Y, 2f * CollisionWorld2D.LinearSlop);
    }

    [Fact]
    public void ABodyWhoseColliderIsOnAnotherEntity_IsRefusedWhenItsEntityJoinsAScene()
    {
        Scene scene = new();

        SceneFixtures.Drifter elsewhere = new(Vector2.Zero);
        BoxCollider2D borrowed = new(new Vector2(8f, 8f));
        elsewhere.Add(borrowed);

        SceneFixtures.Drifter carrier = new(Vector2.Zero);
        carrier.Add(new KinematicBody2D(borrowed));

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => scene.Add(carrier));

        Assert.Contains("same entity", refused.Message, StringComparison.Ordinal);
    }

    // The resting case gravity relies on: the landing step stops a slop short of the floor, and the
    // next step down — already flush — has to report the floor again rather than reading as air.
    [Fact]
    public void ABodySteppingDownOntoAFloor_ReportsItOnTheLandingStepAndOnTheFlushOneAfter()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Stepper body = new(new Vector2(24f, 8f));
        scene.Add(body);

        body.Body.Move(new Vector2(0f, 60f));

        Assert.True(body.Body.IsOnFloor);
        Assert.Equal(new Vector2(0f, -1f), body.Body.FloorNormal);
        Assert.False(body.Body.IsOnWall);
        Assert.False(body.Body.IsOnCeiling);

        body.Body.Move(new Vector2(0f, 60f));

        Assert.True(body.Body.IsOnFloor);
        Assert.Equal(new Vector2(0f, -1f), body.Body.FloorNormal);
        Assert.Equal(24f, body.Position.Y, 2f * CollisionWorld2D.LinearSlop);
    }

    // Airborne, so nothing but the wall can be reported; the normal points back from the wall, which
    // is what tells the caller which side it is on.
    [Theory]
    [InlineData(30f, -1f)]
    [InlineData(-30f, 1f)]
    public void ABodyPushedSidewaysIntoAWall_ReportsAWallOnThatSide(float translation, float expectedNormalX)
    {
        Scene scene = SceneFixtures.Terrain("#..#", "#..#", "#..#");
        Stepper body = new(new Vector2(32f, 24f));
        scene.Add(body);

        body.Body.Move(new Vector2(translation, 0f));

        Assert.True(body.Body.IsOnWall);
        Assert.Equal(expectedNormalX, body.Body.WallNormal.X);
        Assert.False(body.Body.IsOnFloor);
        Assert.Equal(Vector2.Zero, body.Body.FloorNormal);
    }

    [Fact]
    public void ABodyRisingIntoACeiling_ReportsACeilingAndNothingElse()
    {
        Scene scene = SceneFixtures.Terrain("####", "....", "....");
        Stepper body = new(new Vector2(24f, 40f));
        scene.Add(body);

        body.Body.Move(new Vector2(0f, -30f));

        Assert.True(body.Body.IsOnCeiling);
        Assert.False(body.Body.IsOnFloor);
        Assert.False(body.Body.IsOnWall);
    }

    // The axes are swept separately, so one diagonal step can be stopped by two different surfaces.
    [Fact]
    public void ABodySteppingDiagonallyIntoACorner_ReportsBothTheFloorAndTheWall()
    {
        Scene scene = SceneFixtures.Terrain("...#", "...#", "####");
        Stepper body = new(new Vector2(32f, 16f));
        scene.Add(body);

        body.Body.Move(new Vector2(20f, 20f));

        Assert.True(body.Body.IsOnFloor);
        Assert.True(body.Body.IsOnWall);
        Assert.Equal(new Vector2(0f, -1f), body.Body.FloorNormal);
        Assert.Equal(-1f, body.Body.WallNormal.X);
        Assert.False(body.Body.IsOnCeiling);
    }

    // A step that ends exactly flush against a surface records a contact at fraction 1 and is still
    // applied in full, so the span holds a surface that stopped nothing. The axis's blocked flag is
    // the only thing separating the two, and this is the case that says so.
    [Fact]
    public void ABodyStepDownEndingFlushOnAFloor_RecordsTheContactWithoutReportingAFloor()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Stepper body = new(new Vector2(24f, 8f));
        scene.Add(body);

        // The bottom sits exactly 16 above the floor's top face, and the step is exactly 16.
        MoveResult2D result = body.Body.Move(new Vector2(0f, 16f));

        ColliderContact2D[] flush = body.Body.MoveContacts.ToArray();
        Assert.NotEmpty(flush);
        Assert.All(flush, contact => Assert.Equal(new Vector2(0f, -1f), contact.Normal));
        Assert.False(result.BlockedY);
        Assert.Equal(new Vector2(0f, 16f), result.Translation);
        Assert.Equal(24f, body.Position.Y);

        Assert.False(body.Body.IsOnFloor);
        Assert.False(body.Body.IsOnWall);
        Assert.False(body.Body.IsOnCeiling);
        Assert.Equal(Vector2.Zero, body.Body.FloorNormal);
        Assert.Equal(Vector2.Zero, body.Body.WallNormal);
    }

    // The flags are state as of the last move, not a standing description of where the body is: a
    // body that stops pressing into the floor stops reporting one.
    [Fact]
    public void ABodyMovingIntoNothingAfterAMoveThatLanded_ClearsEveryFlag()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Stepper body = new(new Vector2(24f, 8f));
        scene.Add(body);

        body.Body.Move(new Vector2(0f, 60f));
        Assert.True(body.Body.IsOnFloor);

        body.Body.Move(new Vector2(0f, -4f));

        Assert.False(body.Body.IsOnFloor);
        Assert.False(body.Body.IsOnWall);
        Assert.False(body.Body.IsOnCeiling);
        Assert.Equal(Vector2.Zero, body.Body.FloorNormal);
        Assert.Equal(Vector2.Zero, body.Body.WallNormal);
    }

    [Fact]
    public void ABodyLeavingItsScene_ForgetsWhatStoppedItsLastMove()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Stepper body = new(new Vector2(24f, 8f));
        scene.Add(body);

        body.Body.Move(new Vector2(0f, 60f));
        Assert.True(body.Body.IsOnFloor);

        scene.Remove(body);

        Assert.False(body.Body.IsOnFloor);
        Assert.False(body.Body.IsOnWall);
        Assert.False(body.Body.IsOnCeiling);
        Assert.Equal(Vector2.Zero, body.Body.FloorNormal);
        Assert.Equal(Vector2.Zero, body.Body.WallNormal);
    }

    // A contact belongs to the sweep that produced it, and only that sweep's blocked flag says
    // whether it stopped anything. This corner faces up and stopped the X sweep alone; read against
    // the Y flag it would report nothing at all.
    [Fact]
    public void ACornerThatStoppedTheXSweep_IsJudgedByThatAxisAndNotTheOther()
    {
        Scene scene = SceneFixtures.Terrain("....", "...#");
        Ball body = new(new Vector2(44f, 9f));
        scene.Add(body);

        // Right and up: the circle wedges under the block's top-left corner, and nothing stops the
        // rise that follows.
        MoveResult2D result = body.Body.Move(new Vector2(20f, -4f));

        Assert.True(result.BlockedX);
        Assert.False(result.BlockedY);
        Assert.Equal(1, result.XContactCount);
        Assert.Equal(1, result.ContactCount);

        Assert.True(body.Body.IsOnFloor);
        Assert.True(body.Body.FloorNormal.Y < -0.7071f);
        Assert.Equal(body.Body.MoveContacts[0].Normal, body.Body.FloorNormal);
        Assert.False(body.Body.IsOnWall);
        Assert.False(body.Body.IsOnCeiling);
    }

    /// <summary>A 16-unit box on the terrain fixture's "solid" layer, driven one step at a time.</summary>
    private sealed class Stepper : Entity
    {
        internal Stepper(Vector2 position)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(8f, 8f));
            Add(Collider);
            Body = new KinematicBody2D(Collider);
            Body.BlocksOn("solid");
            Add(Body);
        }

        internal BoxCollider2D Collider { get; }

        internal KinematicBody2D Body { get; }
    }

    private sealed class BodyFirst : Entity
    {
        internal BodyFirst(Vector2 position)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(8f, 8f));
            Body = new KinematicBody2D(Collider);
            Body.BlocksOn("solid");

            // The body first: the entity is judged whole when it joins a scene.
            Add(Body);
            Add(Collider);
        }

        internal BoxCollider2D Collider { get; }

        internal KinematicBody2D Body { get; }
    }

    /// <summary>A rounded body, whose corner contacts carry a normal no face declares.</summary>
    private sealed class Ball : Entity
    {
        internal Ball(Vector2 position)
            : base(position)
        {
            CircleCollider2D collider = new(8f);
            Add(collider);
            Body = new KinematicBody2D(collider);
            Body.BlocksOn("solid");
            Add(Body);
        }

        internal KinematicBody2D Body { get; }
    }
}

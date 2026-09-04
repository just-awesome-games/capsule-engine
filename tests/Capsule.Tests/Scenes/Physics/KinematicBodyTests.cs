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
        SceneFixtures.Body body = new(new Vector2(8f, 8f), blocksOn: "solid", bodyFirst: true);

        scene.Add(body);

        MoveResult2D result = body.Mover.Move(new Vector2(0f, 60f));

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
        SceneFixtures.Body body = new(new Vector2(24f, 8f), blocksOn: "solid");
        scene.Add(body);

        body.Mover.Move(new Vector2(0f, 60f));

        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(new Vector2(0f, -1f), body.Mover.FloorNormal);
        Assert.False(body.Mover.IsOnWall);
        Assert.False(body.Mover.IsOnCeiling);

        body.Mover.Move(new Vector2(0f, 60f));

        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(new Vector2(0f, -1f), body.Mover.FloorNormal);
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
        SceneFixtures.Body body = new(new Vector2(32f, 24f), blocksOn: "solid");
        scene.Add(body);

        body.Mover.Move(new Vector2(translation, 0f));

        Assert.True(body.Mover.IsOnWall);
        Assert.Equal(expectedNormalX, body.Mover.WallNormal.X);
        Assert.False(body.Mover.IsOnFloor);
        Assert.Equal(Vector2.Zero, body.Mover.FloorNormal);
    }

    [Fact]
    public void ABodyRisingIntoACeiling_ReportsACeilingAndNothingElse()
    {
        Scene scene = SceneFixtures.Terrain("####", "....", "....");
        SceneFixtures.Body body = new(new Vector2(24f, 40f), blocksOn: "solid");
        scene.Add(body);

        body.Mover.Move(new Vector2(0f, -30f));

        Assert.True(body.Mover.IsOnCeiling);
        Assert.False(body.Mover.IsOnFloor);
        Assert.False(body.Mover.IsOnWall);
    }

    // The axes are swept separately, so one diagonal step can be stopped by two different surfaces.
    [Fact]
    public void ABodySteppingDiagonallyIntoACorner_ReportsBothTheFloorAndTheWall()
    {
        Scene scene = SceneFixtures.Terrain("...#", "...#", "####");
        SceneFixtures.Body body = new(new Vector2(32f, 16f), blocksOn: "solid");
        scene.Add(body);

        body.Mover.Move(new Vector2(20f, 20f));

        Assert.True(body.Mover.IsOnFloor);
        Assert.True(body.Mover.IsOnWall);
        Assert.Equal(new Vector2(0f, -1f), body.Mover.FloorNormal);
        Assert.Equal(-1f, body.Mover.WallNormal.X);
        Assert.False(body.Mover.IsOnCeiling);
    }

    // A step that ends exactly flush against a surface records a contact at fraction 1 and is still
    // applied in full, so the span holds a surface that stopped nothing. The axis's blocked flag is
    // the only thing separating the two, and this is the case that says so.
    [Fact]
    public void ABodyStepDownEndingFlushOnAFloor_RecordsTheContactWithoutReportingAFloor()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        SceneFixtures.Body body = new(new Vector2(24f, 8f), blocksOn: "solid");
        scene.Add(body);

        // The bottom sits exactly 16 above the floor's top face, and the step is exactly 16.
        MoveResult2D result = body.Mover.Move(new Vector2(0f, 16f));

        ColliderContact2D[] flush = body.Mover.MoveContacts.ToArray();
        Assert.NotEmpty(flush);
        Assert.All(flush, contact => Assert.Equal(new Vector2(0f, -1f), contact.Normal));
        Assert.False(result.BlockedY);
        Assert.Equal(new Vector2(0f, 16f), result.Translation);
        Assert.Equal(24f, body.Position.Y);

        Assert.False(body.Mover.IsOnFloor);
        Assert.False(body.Mover.IsOnWall);
        Assert.False(body.Mover.IsOnCeiling);
        Assert.Equal(Vector2.Zero, body.Mover.FloorNormal);
        Assert.Equal(Vector2.Zero, body.Mover.WallNormal);
    }

    // The flags are state as of the last move, not a standing description of where the body is: a
    // body that stops pressing into the floor stops reporting one.
    [Fact]
    public void ABodyMovingIntoNothingAfterAMoveThatLanded_ClearsEveryFlag()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        SceneFixtures.Body body = new(new Vector2(24f, 8f), blocksOn: "solid");
        scene.Add(body);

        body.Mover.Move(new Vector2(0f, 60f));
        Assert.True(body.Mover.IsOnFloor);

        body.Mover.Move(new Vector2(0f, -4f));

        Assert.False(body.Mover.IsOnFloor);
        Assert.False(body.Mover.IsOnWall);
        Assert.False(body.Mover.IsOnCeiling);
        Assert.Equal(Vector2.Zero, body.Mover.FloorNormal);
        Assert.Equal(Vector2.Zero, body.Mover.WallNormal);
    }

    [Fact]
    public void ABodyLeavingItsScene_ForgetsWhatStoppedItsLastMove()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        SceneFixtures.Body body = new(new Vector2(24f, 8f), blocksOn: "solid");
        scene.Add(body);

        body.Mover.Move(new Vector2(0f, 60f));
        Assert.True(body.Mover.IsOnFloor);

        scene.Remove(body);

        Assert.False(body.Mover.IsOnFloor);
        Assert.False(body.Mover.IsOnWall);
        Assert.False(body.Mover.IsOnCeiling);
        Assert.Equal(Vector2.Zero, body.Mover.FloorNormal);
        Assert.Equal(Vector2.Zero, body.Mover.WallNormal);
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
        MoveResult2D result = body.Mover.Move(new Vector2(20f, -4f));

        Assert.True(result.BlockedX);
        Assert.False(result.BlockedY);
        Assert.Equal(1, result.XContactCount);
        Assert.Equal(1, result.ContactCount);

        Assert.True(body.Mover.IsOnFloor);
        Assert.True(body.Mover.FloorNormal.Y < -0.7071f);
        Assert.Equal(body.Mover.MoveContacts[0].Normal, body.Mover.FloorNormal);
        Assert.False(body.Mover.IsOnWall);
        Assert.False(body.Mover.IsOnCeiling);
    }

    // The three answers the query exists to give, from one resting position: into the floor,
    // away from it, and along it. The sideways case is the one a wall probe rests on — a body
    // standing on solid tiles is not blocked by them when it moves parallel to their faces.
    [Theory]
    [InlineData(0f, 4f, true)]
    [InlineData(0f, -4f, false)]
    [InlineData(4f, 0f, false)]
    [InlineData(-4f, 0f, false)]
    public void TestMove_AnswersForABodyRestingOnAFloor(float x, float y, bool expected)
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        SceneFixtures.Body body = new(new Vector2(24f, 24f), blocksOn: "solid");
        scene.Add(body);

        Assert.Equal(expected, body.Mover.TestMove(new Vector2(x, y)));
    }

    // Axis-separated, as a move is: one blocked axis is a blocked test even though the other is free.
    [Fact]
    public void TestMove_OfADiagonalBlockedOnOneAxis_IsBlocked()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        SceneFixtures.Body body = new(new Vector2(24f, 24f), blocksOn: "solid");
        scene.Add(body);

        Assert.True(body.Mover.TestMove(new Vector2(-4f, 4f)));
        Assert.True(body.Mover.TestMove(new Vector2(4f, 4f)));
        Assert.False(body.Mover.TestMove(new Vector2(-4f, -4f)));
    }

    [Fact]
    public void TestMove_LeavesThePositionAndTheLastMovesFlagsAlone()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        SceneFixtures.Body body = new(new Vector2(24f, 8f), blocksOn: "solid");
        scene.Add(body);

        body.Mover.Move(new Vector2(0f, 60f));
        Vector2 landed = body.Position;
        Assert.True(body.Mover.IsOnFloor);

        Assert.True(body.Mover.TestMove(new Vector2(0f, 40f)));
        Assert.False(body.Mover.TestMove(new Vector2(0f, -40f)));

        Assert.Equal(landed, body.Position);
        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(new Vector2(0f, -1f), body.Mover.FloorNormal);
        Assert.False(body.Mover.IsOnWall);
        Assert.False(body.Mover.IsOnCeiling);
    }

    // The corner-correction question: the rise is blocked where the body stands and free four units
    // over, and nothing is moved to find that out.
    [Fact]
    public void TestMove_FromAnOffsetOrigin_AnswersForThatOriginWithoutMovingTheBody()
    {
        Scene scene = SceneFixtures.Terrain("##.#", "....", "####");
        SceneFixtures.Body body = new(new Vector2(28f, 24f), blocksOn: "solid");
        scene.Add(body);

        Assert.True(body.Mover.TestMove(new Vector2(0f, -12f)));
        Assert.False(body.Mover.TestMove(new Vector2(0f, -12f), new Vector2(4f, 0f)));
        Assert.Equal(new Vector2(28f, 24f), body.Position);
    }

    /// <summary>A rounded body, whose corner contacts carry a normal no face declares.</summary>
    private sealed class Ball : Entity
    {
        internal Ball(Vector2 position)
            : base(position)
        {
            CircleCollider2D collider = new(8f);
            Add(collider);
            Mover = new KinematicBody2D(collider);
            Mover.BlocksOn("solid");
            Add(Mover);
        }

        internal KinematicBody2D Mover { get; }
    }
}

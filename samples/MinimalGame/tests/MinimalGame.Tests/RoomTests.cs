using Capsule.Input;
using Capsule.Physics;
using Capsule.Scenes;
using MinimalGame.Game.Entities;

namespace MinimalGame.Tests;

// The room over time through SimulationHost: one scene, stepped on the fixed tick with device
// snapshots in place of a keyboard, and read back through the entity the document placed.
public sealed class RoomTests
{
    // A blocked move comes to rest LinearSlop short of what stopped it, so standing on a surface is
    // only ever true to within that gap.
    private const float RestTolerance = CollisionWorld2D.LinearSlop + 1e-3f;

    private const float Tolerance = 1e-3f;

    // A jump's arc: rise and fall against the default gravity is well under two seconds.
    private const int JumpBudget = 120;

    [Fact]
    public void HoldingRight_WalksThePlayerAtItsWalkSpeedAlongTheFloor()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        float startX = player.Position.X;

        // One snapshot, held for every step: what a key held down for 0.4 s reports.
        const int Steps = 24;
        room.Step(Steps, DeviceSnapshot.Of(Key.D));

        // Speeds are world units per second and a step is a fixed fraction of one, so the distance
        // is the tuning's, not the frame rate's.
        float expected = player.Tuning.WalkSpeed * Steps * (float)room.StepSeconds;
        Assert.Equal(startX + expected, player.Position.X, Tolerance);
        Assert.Equal(RoomFixture.FloorTop, PlayerFeet(player), RestTolerance);
    }

    [Fact]
    public void TappingJump_LeavesTheFloorAndLandsBackOnIt()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        KinematicBody2D body = player.Get<KinematicBody2D>();

        // IsOnFloor is state as of the body's last move, so a jump on the very first step finds no
        // floor to jump from: the script settles for one step first.
        room.Play(new InputScript().Wait(1).Tap(Key.Space).Build());

        Assert.False(body.IsOnFloor, "the tap left the player on the floor");
        Assert.True(room.RunUntil(() => body.IsOnFloor, JumpBudget), "the player never landed");
        Assert.Equal(RoomFixture.FloorTop, PlayerFeet(player), RestTolerance);
    }

    // The ledges collide on their top face and not their underside: the same jump that passes up
    // through one from below is stopped by it on the way down.
    [Fact]
    public void JumpingUnderALedge_PassesThroughItAndLandsOnTop()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        KinematicBody2D body = player.Get<KinematicBody2D>();

        // 128 world units at the walk speed; the hazard on the way reports a contact and stops
        // nothing.
        room.Step(96, DeviceSnapshot.Of(Key.D));

        Assert.Equal(RoomFixture.UnderLedgeX, player.Position.X, Tolerance);
        Assert.Equal(RoomFixture.FloorTop, PlayerFeet(player), RestTolerance);

        room.Step(DeviceSnapshot.Of(Key.Space));

        Assert.True(room.RunUntil(() => body.IsOnFloor, JumpBudget), "the player never landed");
        Assert.Equal(RoomFixture.LedgeTop, PlayerFeet(player), RestTolerance);
    }

    // The hurtbox reports; it blocks nothing. Health is spent on the step the contact is entered
    // and not again while it lasts, and the walk carries on through the hazard.
    [Fact]
    public void WalkingIntoTheHazard_SpendsOneHealthPointAndDoesNotStopTheWalk()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        int startHealth = player.Health;

        Assert.True(
            room.RunUntil(() => player.Health < startHealth, JumpBudget, DeviceSnapshot.Of(Key.D)),
            "the walk never reached the hazard");
        Assert.Equal(startHealth - 1, player.Health);

        float contactX = player.Position.X;
        room.Step(12, DeviceSnapshot.Of(Key.D));

        Assert.True(player.Position.X > contactX, "the hazard stopped the walk");
        Assert.Equal(startHealth - 1, player.Health);
    }

    // Position is the body's top-left corner; the feet are its bottom edge.
    private static float PlayerFeet(Player player) => player.Position.Y + 8f;
}

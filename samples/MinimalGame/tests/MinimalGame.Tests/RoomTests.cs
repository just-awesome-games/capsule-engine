using Capsule.Generated;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tiles;
using MinimalGame.Game;
using MinimalGame.Game.Entities;

namespace MinimalGame.Tests;

// The room over time through SimulationHost: one scene, stepped on the fixed tick with device
// snapshots in place of a keyboard, and read back through the entity the document placed.
public sealed class RoomTests
{
    // A blocked move comes to rest LinearSlop short of what stopped it, so standing on a surface is
    // only ever true to within that gap.
    private const float RestTolerance = CollisionTolerance.LinearSlop + 1e-3f;

    // Four seconds, well past any walk or jump these tests wait on.
    private const int StepBudget = 240;

    // The brick sits in tile row 8, whose cells end at this height.
    private const float BrickRowBottom = 144f;

    // The brick over the spawn breaks at the first head-bump and reads empty from then on, so the
    // second jump rises through the row it filled.
    [Fact]
    public void AJumpFromTheSpawn_BreaksTheBrickOverhead_AndTheNextJumpRisesThroughItsRow()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        KinematicBody2D body = player.Get<KinematicBody2D>();
        TileMap map = room.Scene.FindSingle<TileMap>();
        Assert.Equal(TileTypes.Brick, map.TileAt(2, 8));

        // IsOnFloor is state as of the body's last move, so a jump on the very first step finds no
        // floor to jump from: the script settles for one step first.
        room.Play(new InputScript().Wait(1).Tap(Key.Space).Build());
        Assert.True(room.RunUntil(() => body.IsOnFloor, StepBudget), "the player never landed");
        Assert.Equal(TileGrid.EmptyTileType, map.TileAt(2, 8));

        room.Step(DeviceSnapshot.Of(Key.Space));
        float highestFeet = PlayerFeet(player);
        Assert.True(
            room.RunUntil(
                () =>
                {
                    highestFeet = MathF.Min(highestFeet, PlayerFeet(player));
                    return body.IsOnFloor;
                },
                StepBudget),
            "the player never landed");
        Assert.True(highestFeet < BrickRowBottom, "the second jump never rose past the brick's row");
    }

    // The hurtbox reports; it blocks nothing. Health is spent on the step the contact is entered
    // and not again while it lasts. The hit freezes the room for its tuned steps, and then the walk
    // carries on through the hazard.
    [Fact]
    public void WalkingIntoTheHazard_SpendsOneHealthPointFreezesTheRoomAndDoesNotStopTheWalk()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        int maxHealth = player.Tuning.MaxHealth;

        Assert.True(
            room.RunUntil(() => player.Health < maxHealth, StepBudget, DeviceSnapshot.Of(Key.D)),
            "the walk never reached the hazard");
        Assert.Equal(maxHealth - 1, player.Health);

        float contactX = player.Position.X;
        room.Step(player.Tuning.HurtFreezeTicks, DeviceSnapshot.Of(Key.D));
        Assert.Equal(contactX, player.Position.X);

        room.Step(DeviceSnapshot.Of(Key.D));
        Assert.True(player.Position.X > contactX, "the hazard stopped the walk");
        Assert.Equal(maxHealth - 1, player.Health);
    }

    // The ledges are one-way: the same jump that passes up through one from below is stopped by it
    // on the way down, and down with Jump lets the player fall back through it.
    [Fact]
    public void JumpingUnderALedge_PassesThroughItAndLandsOnTop()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        KinematicBody2D body = player.Get<KinematicBody2D>();

        // The walk passes the hazard on the way, and the hit's freeze holds it for its steps.
        Assert.True(
            room.RunUntil(() => player.Position.X >= RoomFixture.UnderLedgeX, StepBudget, DeviceSnapshot.Of(Key.D)),
            "the walk never reached the ledge");
        Assert.Equal(RoomFixture.FloorTop, PlayerFeet(player), RestTolerance);

        room.Step(DeviceSnapshot.Of(Key.Space));

        Assert.True(room.RunUntil(() => body.IsOnFloor, StepBudget), "the player never landed");
        Assert.Equal(RoomFixture.LedgeTop, PlayerFeet(player), RestTolerance);

        room.Step(DeviceSnapshot.Of(Key.S, Key.Space));

        Assert.True(room.RunUntil(() => body.IsOnFloor, StepBudget), "the player never landed");
        Assert.Equal(RoomFixture.FloorTop, PlayerFeet(player), RestTolerance);
    }

    // Position is the body's top-left corner; the feet are its bottom edge.
    private static float PlayerFeet(Player player) => player.Position.Y + 8f;
}

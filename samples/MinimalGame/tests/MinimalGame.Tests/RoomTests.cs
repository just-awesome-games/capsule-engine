using System.Numerics;
using Capsule;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tiles;
using MinimalGame.Game;
using MinimalGame.Game.Entities;
using MinimalGame.Game.Scenes;
using MinimalGame.Game.Tiles;

namespace MinimalGame.Tests;

// The room over time through SimulationHost: one scene, stepped on the fixed tick with device
// snapshots in place of a keyboard, and read back through the entity the document placed.
public sealed class RoomTests
{
    // A blocked move comes to rest LinearSlop short of what stopped it. Standing on a surface is only
    // ever true to within that gap.
    private const float RestTolerance = CollisionTolerance.LinearSlop + 1e-3f;

    // Four seconds, well past any walk or jump these tests wait on.
    private const int StepBudget = 240;

    // The brick sits in tile row 8, whose cells end at this height.
    private const float BrickRowBottom = 144f;

    // The brick over the spawn breaks at the first head-bump and reads empty from then on. The second
    // jump rises through the row it filled.
    [Fact]
    public void AJumpFromTheSpawn_BreaksTheBrickOverhead_AndTheNextJumpRisesThroughItsRow()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        KinematicBody2D body = player.Get<KinematicBody2D>();
        TileMap map = RoomFixture.TerrainOf(room);
        Assert.IsType<Brick>(map.TileAt(2, 8));

        // IsOnFloor is state as of the body's last move. A jump on the very first step finds no floor
        // to jump from, and the script settles for one step first.
        room.Play(new InputScript().Wait(1).Tap(Key.Space).Build());
        Assert.True(room.RunUntil(() => body.IsOnFloor, StepBudget), "the player never landed");
        Assert.Equal(TileGrid.EmptyTileName, map.TileAt(2, 8).Name);

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

    // A bolt fired up from under the brick breaks it and bursts there, short of its range.
    [Fact]
    public void ABoltFiredAtTheBrick_BreaksIt_AndStopsThere()
    {
        using SimulationHost room = RoomFixture.Simulate();
        TileMap map = RoomFixture.TerrainOf(room);
        Vector2 underTheBrick = new(40f, BrickRowBottom + 8f);
        Bolt bolt = room.Scene.Pool<Bolt>().Take().Fire(underTheBrick, -Vector2.UnitY, BoltTuning.Default);
        room.Scene.Add(bolt);

        Assert.True(room.RunUntil(() => bolt.SceneOrNull is null, StepBudget), "the bolt never ended");
        Assert.Equal(TileGrid.EmptyTileName, map.TileAt(2, 8).Name);
        Assert.True(bolt.Position.Y > BrickRowBottom - 1f, $"the bolt flew on to {bolt.Position.Y}");
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

    // Ordinary ground stops a released walk on the next step. The ice under floor columns 6 to 10
    // (x 96 to 175) carries it on and slows it to a stop over later steps. The ground row is released
    // short of the hazard, and the ice row past it.
    [Theory]
    [InlineData(48f, false)]
    [InlineData(104f, true)]
    public void ReleasingAFullSpeedWalk_StopsAtOnceOnGroundAndSlidesToAStopOnIce(float releaseX, bool slides)
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        TileMap map = RoomFixture.TerrainOf(room);
        Assert.Equal(slides, map.TileAt((int)releaseX / 16, 11) is Ice);

        Assert.True(
            room.RunUntil(() => player.Position.X >= releaseX, StepBudget, DeviceSnapshot.Of(Key.D)),
            "the walk never reached the release point");
        Assert.True(player.IsOnFloor);
        Assert.True(player.Velocity.X > 0f);

        room.Step(DeviceSnapshot.Empty);

        Assert.Equal(slides, player.Velocity.X != 0f);
        Assert.True(room.RunUntil(() => player.Velocity.X == 0f, StepBudget), "the slide never came to a stop");
    }

    // The lamps draw the bolt's glow too. Without them the glow reaches the room's preload only
    // through the player's forwarded bolt pool.
    [Fact]
    public void TheRoom_PreloadsTheBoltsGlowThroughThePlayersPool()
    {
        PlayableScene room = RoomFixture.Compose();
        List<Lamp> lamps = [];
        foreach (Lamp lamp in room.FindAll<Lamp>())
        {
            lamps.Add(lamp);
        }

        foreach (Lamp lamp in lamps)
        {
            room.Remove(lamp);
        }

        Assert.True(room.CollectPreloads().Contains(CapsuleAssets.Textures.GlowTexture));
    }

    // The music's death fade runs on under the pause menu. Quit picked on the step the fade lands
    // leaves the run exiting, and the room asks for no menu after the exit. The first room finds that
    // step, and the second plays the pause and the quit up to it.
    [Fact]
    public void QuittingFromThePauseMenuOnTheStepTheDeathFadeLands_Exits()
    {
        int fadeSteps = 0;
        using (SimulationHost probe = RoomFixture.Simulate())
        {
            SpendAllHealth(probe);
            while (probe.Run.Game.Music.IsFading)
            {
                Assert.True(fadeSteps < StepBudget, "the death fade never landed");
                probe.Step(DeviceSnapshot.Empty);
                fadeSteps++;
            }
        }

        // Escape opens the menu on Resume, and Down moves the focus to Quit before Enter picks it.
        Assert.True(fadeSteps > 3, $"the death fade landed after {fadeSteps} steps, too soon to pause and quit");
        using SimulationHost room = RoomFixture.Simulate();
        SpendAllHealth(room);
        room.Step(DeviceSnapshot.Of(Key.Escape));
        room.Step(DeviceSnapshot.Of(Key.Down));
        room.Step(fadeSteps - 3, DeviceSnapshot.Empty);
        Assert.True(room.Run.Game.Music.IsFading);

        room.Step(DeviceSnapshot.Of(Key.Enter));

        Assert.True(room.Run.ExitRequested, "Quit did not exit");
        Assert.False(room.Run.Game.Music.IsFading);
    }

    // Puts the player in the hazard's box once per grace, ending on the step its last point goes.
    private static void SpendAllHealth(SimulationHost room)
    {
        Player player = RoomFixture.PlayerOf(room);
        Vector2 clear = player.Position;
        Vector2 inHazard = room.Scene.FindSingle<Hazard>().Position + new Vector2(4f, 8f);

        for (int health = player.Health; health > 0; health--)
        {
            RoomFixture.StandAt(room, inHazard);
            Assert.Equal(health - 1, player.Health);
            if (health == 1)
            {
                return;
            }

            RoomFixture.StandAt(room, clear);
            Assert.True(room.RunUntil(() => player.InvulnerableTicksLeft == 0, StepBudget), "the grace never ended");
        }
    }

    // Position is the body's top-left corner, and the feet are its bottom edge.
    private static float PlayerFeet(Player player) => player.Position.Y + 8f;
}

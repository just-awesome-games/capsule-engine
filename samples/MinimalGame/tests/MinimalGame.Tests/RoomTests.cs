using System.Numerics;
using System.Text.Json;
using Capsule;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
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
        Assert.IsType<Brick>(map.TileAt(2, 8));

        // IsOnFloor is state as of the body's last move, so a jump on the very first step finds no
        // floor to jump from: the script settles for one step first.
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
        TileMap map = room.Scene.FindSingle<TileMap>();
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

    // A room torn down with the player inside the camera zone ends the zone's contact after the zone has
    // left the scene, as walking out through the east door does.
    [Fact]
    public void TearingDownTheRoomWithThePlayerInTheCameraZone_Succeeds()
    {
        SimulationHost room = RoomFixture.Simulate();
        RoomFixture.PlayerOf(room).Position = new Vector2(400f, RoomFixture.FloorTop - 8f);
        room.Step(DeviceSnapshot.Empty);

        room.Dispose();
    }

    // Leaving one camera zone for an overlapping one hands the bounds to the zone still held. Leaving
    // that one too brings back the bounds held before the first.
    [Fact]
    public void LeavingACameraZoneIntoAnOverlappingOne_HandsTheBoundsOverAndLeavingBothRestoresTheRoom()
    {
        Rect zoneA = new(160f, 0f, 400f, 192f);
        Rect zoneB = new(320f, 0f, 560f, 192f);
        using SimulationHost room = ZonedRoom(zoneA, zoneB);
        Player player = RoomFixture.PlayerOf(room);
        Rect? roomBounds = room.Scene.Camera.Bounds;
        Assert.NotNull(roomBounds);

        Rect? BoundsWithPlayerAt(float x)
        {
            player.Position = new Vector2(x, RoomFixture.FloorTop - 8f);
            room.Step(DeviceSnapshot.Empty);

            return room.Scene.Camera.Bounds;
        }

        Assert.Equal(zoneA, BoundsWithPlayerAt(200f));
        Assert.Equal(zoneB, BoundsWithPlayerAt(360f));
        Assert.Equal(zoneB, BoundsWithPlayerAt(480f));
        Assert.Equal(roomBounds, BoundsWithPlayerAt(600f));
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

    // A room of bare floor with the player standing west of two camera zones.
    private static SimulationHost ZonedRoom(Rect zoneA, Rect zoneB)
    {
        const int Wide = 40;
        const int High = 12;
        int[] tiles = new int[Wide * High];
        Array.Fill(tiles, 1, (High - 1) * Wide, Wide);
        TileGrid floor = new(16, Wide, High, [TileGrid.EmptyTile, new TileType { Name = "ground", Layer = CollisionLayers.Solid }], tiles);

        EntityPlacement Zone(int id, Rect area) => new(
            id,
            "camera-zone",
            area.Left,
            area.Top,
            Properties: JsonSerializer.SerializeToElement(new { size = new[] { area.Size.X, area.Size.Y } }));

        SceneDocument document = new(
            [new TileMapPlacement(1, floor), new EntityPlacement(2, "player", 32f, RoomFixture.FloorTop - 8f), Zone(3, zoneA), Zone(4, zoneB)],
            nextEntityId: 5,
            settings: new SceneSettings { Properties = JsonSerializer.SerializeToElement(new { music = "audio/music/room.ogg" }) });

        Run run = new();
        GameBoot.Start(run);

        return new SimulationHost(CapsuleScenes.Registry.Create(CapsuleAssets.Scenes.RoomScene, document), run: run);
    }

    // Position is the body's top-left corner; the feet are its bottom edge.
    private static float PlayerFeet(Player player) => player.Position.Y + 8f;
}

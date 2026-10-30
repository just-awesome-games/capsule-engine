using System.Numerics;
using System.Text.Json;
using Capsule;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using MinimalGame.Game;
using MinimalGame.Game.Entities;

namespace MinimalGame.Tests;

// The camera zones over time: a bare floor with the zones a test places, and the room the game ships.
public sealed class CameraZoneTests
{
    // Four seconds, well past any slide these tests wait on.
    private const int StepBudget = 240;

    // Leaving one camera zone for an overlapping one hands the bounds to the zone still held. Leaving
    // that one too brings back the bounds held before the first.
    [Fact]
    public void LeavingACameraZoneIntoAnOverlappingOne_HandsTheBoundsOverAndLeavingBothRestoresTheRoom()
    {
        Rect zoneA = Rect.FromEdges(160f, 0f, 400f, 192f);
        Rect zoneB = Rect.FromEdges(320f, 0f, 560f, 192f);
        using SimulationHost room = ZonedRoom([zoneA, zoneB]);
        Rect? roomBounds = room.Scene.Camera.Bounds;
        Assert.NotNull(roomBounds);

        Assert.Equal(zoneA, RoomFixture.BoundsWithPlayerAt(room, 200f));
        Assert.Equal(zoneB, RoomFixture.BoundsWithPlayerAt(room, 360f));
        Assert.Equal(zoneB, RoomFixture.BoundsWithPlayerAt(room, 480f));
        Assert.Equal(roomBounds, RoomFixture.BoundsWithPlayerAt(room, 600f));
    }

    // Two zones that share an edge hold the player together on that edge. Stepping off it into one hands
    // the bounds to that one, and leaving both in a single step brings back the room. The player's 8
    // unit body is centred on each x.
    [Fact]
    public void LeavingTwoAdjacentCameraZonesInOneStep_RestoresTheRoom_AndLeavingOneHandsTheBoundsToTheOther()
    {
        Rect zoneA = Rect.FromEdges(160f, 0f, 320f, 192f);
        Rect zoneB = Rect.FromEdges(320f, 0f, 480f, 192f);
        using SimulationHost room = ZonedRoom([zoneA, zoneB]);
        Rect? roomBounds = room.Scene.Camera.Bounds;
        Rect?[] onEither = [zoneA, zoneB];

        Assert.Contains(RoomFixture.BoundsWithPlayerAt(room, 316f), onEither);
        Assert.Equal(zoneA, RoomFixture.BoundsWithPlayerAt(room, 236f));
        Assert.Contains(RoomFixture.BoundsWithPlayerAt(room, 316f), onEither);
        Assert.Equal(zoneB, RoomFixture.BoundsWithPlayerAt(room, 396f));
        Assert.Contains(RoomFixture.BoundsWithPlayerAt(room, 316f), onEither);
        Assert.Equal(roomBounds, RoomFixture.BoundsWithPlayerAt(room, 596f));
    }

    // The zone's first slide-in holds the player until the view lands on it. Stepping back in slides the
    // view again and leaves the player in control.
    [Fact]
    public void EnteringACameraZoneTheFirstTime_HoldsThePlayerUntilTheViewLands()
    {
        using SimulationHost room = ZonedRoom([Rect.FromEdges(160f, 0f, 400f, 192f)]);
        Player player = RoomFixture.PlayerOf(room);
        Camera camera = room.Scene.Camera;
        room.Step(DeviceSnapshot.Empty);

        RoomFixture.StandAt(room, 200f);
        Assert.False(player.HasControl);
        Assert.True(room.RunUntil(() => !camera.IsTransitioning, StepBudget), "the view never landed");
        Assert.False(player.HasControl);
        room.Step(DeviceSnapshot.Empty);
        Assert.True(player.HasControl);

        RoomFixture.StandAt(room, 32f);
        RoomFixture.StandAt(room, 200f);
        Assert.True(camera.IsTransitioning);
        Assert.True(player.HasControl);
    }

    // A room torn down with the player inside the camera zone ends the zone's contact after the zone has
    // left the scene, as walking out through the east door does, and the teardown throws nothing.
    [Fact]
    public void TearingDownTheRoomWithThePlayerInTheCameraZone_Succeeds()
    {
        SimulationHost room = RoomFixture.Simulate();
        RoomFixture.StandAt(room, 400f);

        Assert.Null(Record.Exception(room.Dispose));
    }

    // A room of bare floor with the player standing at its west end and the camera zones placed.
    private static SimulationHost ZonedRoom(Rect[] zones)
    {
        const int Wide = 40;
        const int High = 12;
        int[] tiles = new int[Wide * High];
        Array.Fill(tiles, 1, (High - 1) * Wide, Wide);

        SceneDocumentEntry floor = new(
            "tile-map",
            JsonSerializer.SerializeToElement(new
            {
                tileSize = 16,
                width = Wide,
                height = High,
                tileTypes = new object[] { new { name = "empty" }, new { name = "ground", layer = CollisionLayers.Solid } },
                tiles,
                collider = true,
            }));

        SceneDocumentEntry Zone(Rect area) => new(
            "camera-zone",
            new EntitySpawn(area.Position),
            JsonSerializer.SerializeToElement(new { size = new[] { area.Size.X, area.Size.Y } }));

        List<SceneDocumentEntry> entries = [floor, new SceneDocumentEntry("player", new EntitySpawn(new Vector2(32f, RoomFixture.FloorTop - 8f)))];
        foreach (Rect zone in zones)
        {
            entries.Add(Zone(zone));
        }

        SceneDocument document = new(
            entries,
            JsonSerializer.SerializeToElement(new { camera = new { type = "game-camera" }, track = "audio/music/room.ogg" }));

        Run run = new();
        run.StartGame(static run => new MinimalGameInstance(run));

        return new SimulationHost(CapsuleScenes.Registry.Create(CapsuleAssets.Scenes.RoomScene, document), run: run);
    }
}

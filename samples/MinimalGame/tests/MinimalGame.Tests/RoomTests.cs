using System.Numerics;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using MinimalGame.Game.Entities;

namespace MinimalGame.Tests;

// The room over time through SimulationHost: one scene, stepped on the fixed tick with device
// snapshots in place of a keyboard, and read back through the entity the document placed.
public sealed class RoomTests
{
    // A blocked move comes to rest LinearSlop short of what stopped it, so standing on a surface is
    // only ever true to within that gap.
    private const float RestTolerance = CollisionTolerance.LinearSlop + 1e-3f;

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

    // The spark is placed by the entity tree, a pivot child turning under the hazard with the spark
    // under that at its orbit radius, so the drawn frame sits one radius from the hazard's centre
    // and moves round it step by step, headless exactly as windowed.
    [Fact]
    public void TheHazardsSpark_OrbitsTheHazardInTheDrawnFrame()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Hazard hazard = room.Scene.FindSingle<Hazard>();
        Vector2 centre = hazard.Position + new Vector2(8f, 12f);

        room.Step();
        Vector2 first = Spark(room).Position;
        room.Step(30);
        Vector2 later = Spark(room).Position;

        Assert.Equal(20f, Vector2.Distance(centre, first), Tolerance);
        Assert.Equal(20f, Vector2.Distance(centre, later), Tolerance);
        Assert.True(Vector2.Distance(first, later) > 1f, "the spark never moved round the hazard");
    }

    // The player faces by the scale of the pivot its sprite hangs on, so the frame the room draws
    // is mirrored while walking left and upright again walking right; the body under it is
    // neither turned nor scaled.
    [Fact]
    public void WalkingLeft_MirrorsThePlayersFrameAboutItsPivotAndLeavesTheBodyUpright()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);

        room.Step(DeviceSnapshot.Of(Key.A));
        SpriteIntent facingLeft = PlayerFrame(room);
        room.Step(DeviceSnapshot.Of(Key.D));
        SpriteIntent facingRight = PlayerFrame(room);

        Assert.True(facingLeft.FlipX, "walking left did not mirror the frame");
        Assert.False(facingRight.FlipX, "walking right did not restore the frame");
        Assert.True(facingLeft.Size.X > 0f && facingRight.Size.X > 0f, "a mirrored frame lost its extent");
        Assert.Equal(Vector2.One, player.WorldTransform.Scale);
        Assert.Equal(0f, player.WorldTransform.Rotation);
    }

    // The take-off stretch is the visual's reaction to the fact the root published this step: the
    // frame drawn after the jump step is the frame's texels at the tuning's stretch, and the body
    // beneath it is no taller.
    [Fact]
    public void Jumping_StretchesTheDrawnFrameByTheTuningAndLeavesTheBodyAlone()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);

        room.Step(2);
        room.Step(DeviceSnapshot.Of(Key.Space));
        SpriteIntent stretched = PlayerFrame(room);

        Assert.True(player.JumpedThisStep, "the tap did not take off");
        Assert.Equal(stretched.Sprite.Region.Width * player.Tuning.JumpStretch.X, stretched.Size.X, Tolerance);
        Assert.Equal(stretched.Sprite.Region.Height * player.Tuning.JumpStretch.Y, stretched.Size.Y, Tolerance);
        Assert.Equal(Vector2.One, player.WorldTransform.Scale);
    }

    // The bolt leaves from the muzzle socket of the frame drawn, the right edge of the 8x8 body at
    // mid-height on the idle frame. The socket is a point on the
    // frame: the walk's bob shows in the muzzle's height on the frame that carries it.
    [Fact]
    public void PressingShoot_FiresABoltFromTheMuzzleSocketOfTheFrameDrawn()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);

        // The landing squash on the first step has recovered by here; the frame is still idle-0.
        room.Step(20);
        room.Step(DeviceSnapshot.Empty.With(MouseButton.Left));
        Bolt bolt = room.Scene.FindSingle<Bolt>();

        Assert.True(player.ShotThisStep, "the press did not fire");
        Assert.Equal(player.Position + new Vector2(8f, 4f), bolt.Position);
        Assert.Equal(player.Muzzle.WorldPosition, bolt.Position);

        // Walking left mirrors the muzzle with the frame: the second frame of the walk (walk-1) sets
        // it a texel higher, and the facing scale carries it to the body's left edge.
        room.Step(7, DeviceSnapshot.Of(Key.A));
        Assert.Equal(player.Position + new Vector2(0f, 3f), player.Muzzle.WorldPosition);

        room.Step(DeviceSnapshot.Empty.With(MouseButton.Left).With(Key.A));
        Bolt second = room.Scene.Entities.ToArray().OfType<Bolt>().Last();

        Assert.NotSame(bolt, second);
        Assert.Equal(player.Muzzle.WorldPosition, second.Position);
    }

    // Once a bolt has run its lifetime and returned to the pool, the next shot reuses that same
    // instance rather than building another, and it draws at the muzzle with no smear from where it
    // last flew.
    [Fact]
    public void FiringAgainAfterABoltExpires_ReusesItFromTheMuzzleWithNoSmear()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);

        room.Step(20);
        room.Step(DeviceSnapshot.Empty.With(MouseButton.Left));
        Bolt first = room.Scene.FindSingle<Bolt>();

        room.Step(BoltTuning.Default.LifetimeTicks + 1);
        Assert.Empty(room.Scene.Entities.ToArray().OfType<Bolt>());

        room.Step(DeviceSnapshot.Empty.With(MouseButton.Left));
        Bolt second = room.Scene.FindSingle<Bolt>();

        Assert.Same(first, second);
        Assert.Equal(player.Muzzle.WorldPosition, second.Position);

        SpriteIntent frame = Assert.Single(
            room.Simulation.View.Sprites.ToArray(),
            sprite => sprite.Position == second.Position);
        Assert.Equal(frame.Position, frame.PreviousPosition);
    }

    // Position is the body's top-left corner; the feet are its bottom edge.
    private static float PlayerFeet(Player player) => player.Position.Y + 8f;

    private static SpriteIntent Spark(SimulationHost room) =>
        Assert.Single(
            room.Simulation.View.Sprites.ToArray(),
            sprite => sprite.Size == new Vector2(4f, 4f) && sprite.Sprite.Texture == CapsuleAssets.Textures.Hazard);

    // The one frame drawn from the player's sheet.
    private static SpriteIntent PlayerFrame(SimulationHost room) =>
        Assert.Single(room.Simulation.View.Sprites.ToArray(), sprite => sprite.Sprite.Texture == CapsuleAssets.Textures.Actors.Player);
}

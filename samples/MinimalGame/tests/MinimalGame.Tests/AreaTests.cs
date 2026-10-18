using System.Numerics;
using Capsule;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Particles;
using Capsule.Physics;
using Capsule.Scenes;
using MinimalGame.Game;
using MinimalGame.Game.Entities;

namespace MinimalGame.Tests;

// The areas that act on the player inside them: the hazard that hurts it, and the gust and the
// waterfall that push it.
public sealed class AreaTests
{
    // Four seconds, well past any walk these tests wait on.
    private const int StepBudget = 240;

    // The hurtbox reports and blocks nothing. Health is spent on the step the contact is entered
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

    // The first touch frees the spark from its orbit. It flies on along the tangent it was moving in at the
    // speed it left at, and leaves the scene past the view. The box keeps hurting.
    [Fact]
    public void TheHazardsSpark_BreaksLooseOnTheFirstTouch_AndFliesOnAlongItsTangentUntilItLeavesTheView()
    {
        using SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        Hazard hazard = room.Scene.FindSingle<Hazard>();
        Entity spark = SparkUnder(hazard) ?? throw new InvalidOperationException("The hazard carries no spark. Restore the Spark child the Hazard constructor adds.");
        Vector2 before = spark.WorldPosition;
        Vector2 heading = default;

        Assert.True(
            room.RunUntil(
                () =>
                {
                    heading = spark.WorldPosition - before;
                    before = spark.WorldPosition;
                    return spark.Parent is null;
                },
                StepBudget,
                DeviceSnapshot.Of(Key.D)),
            "the spark never broke loose");
        Assert.Null(spark.Parent);

        // The room is frozen for the hit, and the spark with it.
        room.Step(player.Tuning.HurtFreezeTicks);
        Vector2 first = spark.WorldPosition;
        room.Step();
        Vector2 second = spark.WorldPosition;
        room.Step();
        Vector2 third = spark.WorldPosition;

        Assert.True(Vector2.Dot(Vector2.Normalize(second - first), Vector2.Normalize(heading)) > 0.99f, "the spark left its tangent");
        Assert.Equal((second - first).Length(), (third - second).Length(), 1e-3f);
        Assert.True(room.RunUntil(() => spark.SceneOrNull is null, StepBudget), "the spark never left the view");
        Assert.True(hazard.Get<BoxCollider2D>().Enabled);
    }

    // The waterfall holds the player to its gravity while the player is in it and lets go on leaving. A room
    // torn down with the player inside ends that contact too.
    [Fact]
    public void TheWaterfall_SetsThePlayersGravityWhileThePlayerIsInIt()
    {
        SimulationHost room = RoomFixture.Simulate();
        Player player = RoomFixture.PlayerOf(room);
        Waterfall falls = room.Scene.FindSingle<Waterfall>();
        Vector2 inside = falls.Position + new Vector2(4f, 8f);

        RoomFixture.StandAt(room, inside);
        Assert.Equal(falls.Gravity, player.GravityOverride);

        RoomFixture.StandAt(room, new Vector2(100f, inside.Y));
        Assert.Null(player.GravityOverride);

        RoomFixture.StandAt(room, inside);
        Assert.Null(Record.Exception(room.Dispose));
    }

    // The hall's gust blows from the first step the player is in it, falls calm after its blowing steps, and
    // blows again after its calm ones. It stops on the player leaving.
    [Fact]
    public void TheHallsGust_PushesThePlayerInItWhileItBlows()
    {
        Run run = new();
        GameBoot.Start(run);
        using SimulationHost hall = new(CapsuleScenes.Registry.Create(CapsuleAssets.Scenes.Halls.HallScene), run: run);
        Player player = hall.Scene.FindSingle<Player>();
        Gust gust = hall.Scene.FindSingle<Gust>();
        Assert.True(gust.CalmTicks > 0, "the hall's gust blows throughout");

        Vector2 inside = gust.Position + new Vector2(28f, 4f);
        RoomFixture.StandAt(hall, inside);
        Assert.Equal(gust.Velocity, player.Wind);
        RoomFixture.StandAt(hall, inside, gust.BlowTicks - 1);
        Assert.Equal(Vector2.Zero, player.Wind);
        RoomFixture.StandAt(hall, inside, gust.CalmTicks);
        Assert.Equal(gust.Velocity, player.Wind);
        RoomFixture.StandAt(hall, gust.Position + new Vector2(gust.Size.X + 8f, 4f));
        Assert.Equal(Vector2.Zero, player.Wind);
    }

    // The spark is the one entity under the hazard that trails particles.
    private static Entity? SparkUnder(Entity entity)
    {
        foreach (Entity child in entity.Children)
        {
            if ((child.Has<ParticleEmitter>() ? child : SparkUnder(child)) is { } spark)
            {
                return spark;
            }
        }

        return null;
    }
}

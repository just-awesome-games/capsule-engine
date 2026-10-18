using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

// One-way surfaces, a tile and a collider alike, block only a mover coming down onto them from above.
public sealed class OneWayTests
{
    // The ledge's top lies along y = 32 and the ground's along y = 80. The body jumps up through the
    // ledge, lands on it, drops through it and lands on the ground.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AOneWaySurface_PassesFromBelow_LandsFromAbove_AndDropsThrough(bool collider)
    {
        Scene scene = (SceneFixtures.Terrain(
            "....",
            "....",
            collider ? "...." : "----",
            "....",
            "....",
            "####")).Started();
        if (collider)
        {
            scene.Add(new Slab(new Vector2(0f, 32f), CollisionFixtures.Solid, new Vector2(64f, 8f), oneWay: true));
        }

        SceneFixtures.Body body = new(new Vector2(24f, 56f), blocksOn: "solid");
        scene.Add(body);

        Assert.False(body.Mover.Move(new Vector2(0f, -40f)).Blocked);
        Assert.Equal(16f, body.Position.Y, CollisionFixtures.Tolerance);

        body.Mover.Move(new Vector2(0f, 40f));
        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(24f, body.Position.Y, CollisionFixtures.Tolerance);

        body.Mover.DropThrough();
        Assert.False(body.Mover.Move(new Vector2(0f, 2f)).Blocked);
        body.Mover.Move(new Vector2(0f, 60f));
        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(72f, body.Position.Y, CollisionFixtures.Tolerance);
    }

    // The girder run spans x = 32 to 80 from y = 32 down to 48, or to 40 for the half-height tiles. A
    // body rising through the run while moving right crosses both interior seams without catching, lands
    // on top, and drops back through. A body moving right at the run's height meets its end as a wall.
    [Theory]
    [InlineData("..===...", false)]
    [InlineData("..~~~...", false)]
    [InlineData("........", true)]
    public void ASolidSidedRun_PassesFromBelowAcrossItsSeams_WallsItsEnds_AndDropsThrough(string run, bool collider)
    {
        Scene scene = SceneFixtures.Terrain(
            "........",
            "........",
            run,
            "........",
            "........",
            "########").Started();
        if (collider)
        {
            scene.Add(new Slab(new Vector2(32f, 32f), CollisionFixtures.Solid, new Vector2(48f, 16f), oneWay: true, solidSides: true));
        }

        SceneFixtures.Body climber = new(new Vector2(34f, 72f), blocksOn: "solid");
        SceneFixtures.Body walker = new(new Vector2(18f, 32f), blocksOn: "solid");
        scene.Add(climber);
        scene.Add(walker);

        Assert.False(climber.Mover.Move(new Vector2(24f, -48f)).Blocked);
        climber.Mover.Move(new Vector2(0f, 40f));
        Assert.True(climber.Mover.IsOnFloor);
        Assert.Equal(58f, climber.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(24f, climber.Position.Y, CollisionFixtures.Tolerance);

        walker.Mover.Move(new Vector2(20f, 0f));
        Assert.True(walker.Mover.IsOnWall);
        Assert.Equal(new Vector2(-1f, 0f), walker.Mover.WallNormal);
        Assert.Equal(24f, walker.Position.X, CollisionFixtures.Tolerance);

        climber.Mover.DropThrough();
        Assert.False(climber.Mover.Move(new Vector2(0f, 2f)).Blocked);
        climber.Mover.Move(new Vector2(0f, 60f));
        Assert.True(climber.Mover.IsOnFloor);
        Assert.Equal(72f, climber.Position.Y, CollisionFixtures.Tolerance);
    }

    // A one-way pusher meets a body only as a body landing on it would. Sinking onto a body below, it
    // passes through instead of shoving it down.
    [Fact]
    public void AOneWayColliderSinkingOntoABody_PassesThroughItWithoutAShove()
    {
        Scene scene = new();
        Slab slab = new(new Vector2(0f, 32f), CollisionFixtures.Solid, new Vector2(64f, 8f), oneWay: true);
        SceneFixtures.Body body = new(new Vector2(24f, 44f), blocksOn: "solid");
        body.Mover.MovedBy = new("solid");
        bool crushed = false;
        body.Mover.Crushed += _ => crushed = true;
        scene.Add(slab);
        scene.Add(body);

        slab.Position += new Vector2(0f, 10f);

        Assert.Equal(new Vector2(24f, 44f), body.Position);
        Assert.False(crushed);
    }

    // A one-way slope's edge runs from (16, 32) up to (32, 16). A box beside its low end and reaching
    // below its line passes the end sideways, as it passes the rest of a one-way surface.
    [Fact]
    public void AOneWaySlope_LetsABoxPassItsLowEndSideways()
    {
        CollisionWorld2D world = new();
        world.AddGrid(
            CollisionFixtures.TileSize,
            3,
            3,
            [0, 0, 0, 0, 1, 0, 0, 0, 0],
            [new CellProfile2D(null), new CellProfile2D(world.Layer(CollisionFixtures.Ledge), CollisionFixtures.SlopeUp, OneWay: true)]);

        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(8f, 28f, 4f, 12f),
            new Vector2(12f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.False(result.Blocked);
        Assert.Equal(new Vector2(12f, 0f), result.Translation);
    }

    // The surface lies along y = 16, and the box reaches 6 below it. A one-way surface blocks only a shape
    // coming down onto it from above. A shape that starts inside it reports nothing.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AShapeCastStartingInsideAOneWaySurface_PassesIt(bool collider)
    {
        Scene scene = SceneFixtures.Terrain("....", collider ? "...." : "----", "####").Started();
        if (collider)
        {
            scene.Add(new Slab(new Vector2(0f, 16f), CollisionFixtures.Solid, new Vector2(64f, 8f), oneWay: true));
        }

        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        Vector2 origin = new(20f, 14f);

        Assert.False(scene.Collision.ShapeCast(box, origin, Vector2.Zero, CollisionFilter.Everything, out _));
        Assert.False(scene.Collision.ShapeCast(box, origin, new Vector2(0f, 8f), CollisionFilter.Everything, out _));
    }
}

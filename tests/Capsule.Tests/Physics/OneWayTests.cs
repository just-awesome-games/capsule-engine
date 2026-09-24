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
        Scene scene = SceneFixtures.Terrain(
            "....",
            "....",
            collider ? "...." : "----",
            "....",
            "....",
            "####");
        if (collider)
        {
            scene.Add(new Slab(new Vector2(0f, 32f)));
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
            "########");
        if (collider)
        {
            scene.Add(new Slab(new Vector2(32f, 32f), new Vector2(48f, 16f), solidSides: true));
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
        Slab slab = new(new Vector2(0f, 32f));
        SceneFixtures.Body body = new(new Vector2(24f, 44f), blocksOn: "solid");
        body.Mover.MovedBy("solid");
        bool crushed = false;
        body.Mover.Crushed += _ => crushed = true;
        scene.Add(slab);
        scene.Add(body);

        slab.Position += new Vector2(0f, 10f);

        Assert.Equal(new Vector2(24f, 44f), body.Position);
        Assert.False(crushed);
    }

    // A solid tile above a one-way tile culls its edge along y = 16. A filter that cannot see the solid
    // tile turns it back into empty space, and every query meets the edge again.
    [Fact]
    public void AOneWayEdgeCulledByANeighbourTheFilterExcludes_IsLiveToCastRayAndOverlap()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "=", "-");
        CollisionFilter platform = world.CreateFilter(CollisionFixtures.Platform);
        Vector2 up = new(0f, -1f);

        Assert.True(world.ShapeCast(Shape2D.Box(Vector2.Zero, new Vector2(4f, 4f)), new Vector2(6f, 4f), new Vector2(0f, 20f), platform, out ShapeCastHit2D cast));
        Assert.Equal(up, cast.Normal);

        Assert.True(world.Raycast(new Vector2(8f, -10f), Vector2.UnitY, 40f, platform, out RayHit2D ray));
        Assert.Equal(26f, ray.Distance, 3);

        Span<Contact2D> contacts = stackalloc Contact2D[4];
        Assert.Equal(1, world.OverlapBoxAll(CollisionFixtures.Box(4f, 10f, 8f, 8f), platform, contacts));
        Assert.Equal(up, contacts[0].Normal);
    }

    /// <summary>A one-way box on the layer "solid", 64 wide and 8 tall unless sized.</summary>
    private sealed class Slab : Entity
    {
        internal Slab(Vector2 position, Vector2? size = null, bool solidSides = false)
            : base(position)
        {
            Add(new BoxCollider2D(size ?? new Vector2(64f, 8f)) { Layer = "solid", OneWay = true, SolidSides = solidSides });
        }
    }
}

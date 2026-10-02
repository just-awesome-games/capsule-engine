using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

using Body = Capsule.Tests.Scenes.SceneFixtures.Body;

namespace Capsule.Tests.Physics;

public sealed class ColliderCastTests
{
    [Fact]
    public void Move_LeavesTheEntityWhereTheSweepStoppedAndNamesWhatStoppedIt()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Body body = new(new Vector2(8f, 8f));
        body.Mover.BlockedBy = new("solid");
        scene.Add(body);

        MoveResult2D result = body.Mover.Move(new Vector2(0f, 60f));

        Assert.True(result.Blocked);
        Assert.Equal(24f, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.NotEmpty(body.Mover.MoveContacts.ToArray());
        Assert.All(
            body.Mover.MoveContacts.ToArray(),
            contact =>
            {
                Assert.True(contact.Tile.HasValue);
                Assert.Equal("solid", contact.LayerName);
                Assert.Equal(new Vector2(0f, -1f), contact.Normal);
            });
    }

    [Fact]
    public void ADetectedContact_DoesNotBlockAMoverThatDoesNotBlockOnItsLayer()
    {
        Scene scene = new();
        Body player = new(Vector2.Zero);
        Body enemy = new(new Vector2(10f, 0f));
        player.Collider.Layer = "player";
        player.Collider.Detects = new("enemy");
        player.Collider.ReportsContacts = true;
        player.Mover.BlockedBy = new("solid");
        enemy.Collider.Layer = "enemy";

        List<ColliderContact2D> entered = [];
        player.Collider.ContactEntered += entered.Add;

        scene.Add(player);
        scene.Add(enemy);
        using SceneSimulation simulation = new(scene);

        MoveResult2D result = player.Mover.Move(new Vector2(12f, 0f));
        simulation.Step(SceneFixtures.Step(0));

        Assert.False(result.Blocked);
        Assert.Equal(new Vector2(12f, 0f), player.Position);
        Assert.Empty(player.Mover.MoveContacts.ToArray());

        ColliderContact2D contact = Assert.Single(entered);
        Assert.Same(enemy, contact.OtherEntity);
        Assert.Same(enemy.Collider, contact.OtherCollider);
        Assert.Null(contact.Tile);
        Assert.Equal(scene.Collision.Layer("enemy"), contact.Layer);
        Assert.Equal("enemy", contact.LayerName);
    }

    // The per-move filter is for the step, not for the mover: what it blocks on afterwards is
    // whatever BlockedBy last said.
    [Fact]
    public void Move_WithABlockingFilter_HonoursItOverBlockedByAndLeavesTheStandingFilterAlone()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Body body = new(new Vector2(8f, 8f));
        body.Mover.BlockedBy = new("solid");
        scene.Add(body);

        CollisionFilter standing = body.Mover.Filter;

        // Blocking on nothing for this call alone: the floor the mover normally stops on is not
        // there as far as this move is concerned.
        MoveResult2D through = body.Mover.Move(new Vector2(0f, 60f), CollisionFilter.None);

        Assert.False(through.Blocked);
        Assert.Equal(68f, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.Equal(standing, body.Mover.Filter);

        // And the next plain move resolves against the standing filter again.
        body.Teleport(new Vector2(8f, 8f));
        Assert.True(body.Mover.Move(new Vector2(0f, 60f)).Blocked);
    }

    // The whole value of the sweep as a probe: a surface it runs along is not in its way. The body
    // rests on the floor and stands flush against the wall, so the two cases differ only in which
    // way the sweep goes.
    [Fact]
    public void Cast_MeetsTheSurfaceItDrivesIntoAndNotTheOnesItRunsAlong()
    {
        Scene scene = SceneFixtures.Terrain("..#.", "..#.", "####");
        Prober prober = new(new Vector2(24f, 24f), new Vector2(8f, 8f), "solid");
        scene.Add(prober);

        Assert.True(prober.Collider.Cast(new Vector2(6f, 0f), out ShapeCastHit2D wall));
        Assert.Equal(0f, wall.Fraction);
        Assert.Equal(new Vector2(-1f, 0f), wall.Normal);

        Assert.True(prober.Collider.Cast(new Vector2(0f, 6f), out ShapeCastHit2D floor));
        Assert.Equal(0f, floor.Fraction);
        Assert.Equal(new Vector2(0f, -1f), floor.Normal);

        // Away from the wall and along the floor: nothing is met, and the floor the body stands on
        // is not reported for having been beside the sweep.
        Assert.False(prober.Collider.Cast(new Vector2(-6f, 0f), out _));
    }

    // The rounded shapes go through the iterated narrowphase rather than the closed-form box path,
    // and the tangential rule has to read the same there.
    [Fact]
    public void Cast_OfARoundedColliderAlongAFloorItRestsOn_MeetsNothing()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        RoundProber prober = new(new Vector2(24f, 24f), 8f, "solid");
        scene.Add(prober);

        Assert.False(prober.Collider.Cast(new Vector2(6f, 0f), out _));
        Assert.False(prober.Collider.Cast(new Vector2(-6f, 0f), out _));
        Assert.True(prober.Collider.Cast(new Vector2(0f, 6f), out _));
    }

    // The sweep starts on top of the collider's own shape, so without the exclusion every cast would
    // report itself at fraction 0 and never reach anything beyond.
    [Fact]
    public void Cast_MeetsAnotherColliderAndNeverItself()
    {
        Scene scene = new();
        Prober prober = new(Vector2.Zero, new Vector2(8f, 8f), CollisionWorld2D.DefaultLayerName);
        Prober other = new(new Vector2(20f, 0f), new Vector2(8f, 8f), CollisionWorld2D.DefaultLayerName);
        scene.Add(prober);
        scene.Add(other);

        Assert.True(prober.Collider.Cast(new Vector2(40f, 0f), out ShapeCastHit2D hit));
        Assert.Equal(other.Collider.Handle, hit.Target.Collider);
        Assert.Equal(0.3f, hit.Fraction, 1e-4f);

        Assert.False(prober.Collider.Cast(new Vector2(-40f, 0f), out _));
    }

    // The wall's right face lies along x = 32. Each shape starts centred 2 inside it and reaches 6 in,
    // then starts touching it from outside. The box takes the closed form. The triangle overlaps the
    // wall hull to hull, where the narrowphase measures no depth, and the circle by its radius.
    [Theory]
    [InlineData("box")]
    [InlineData("triangle")]
    [InlineData("circle")]
    public void ShapeCast_StartingInsideAColliderReportsItSweepingAway_AndStartingTouchingPassesIt(string kind)
    {
        CollisionWorld2D world = new();
        ColliderHandle wall = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(32f, 64f)), Vector2.Zero, world.Layer("solid"));
        Shape2D shape = kind switch
        {
            "box" => Shape2D.Box(new Vector2(-4f, -4f), new Vector2(8f, 8f)),
            "triangle" => Shape2D.Polygon([new(-4f, 4f), new(4f, 4f), new(0f, -4f)]),
            _ => Shape2D.Circle(Vector2.Zero, 4f),
        };
        Vector2 away = new(40f, 0f);

        Assert.True(world.ShapeCast(shape, new Vector2(30f, 32f), away, CollisionFilter.Everything, out ShapeCastHit2D hit));
        Assert.Equal(wall, hit.Target.Collider);
        Assert.Equal(0f, hit.Fraction);
        Assert.Equal(new Vector2(1f, 0f), hit.Normal);

        Assert.False(world.ShapeCast(shape, new Vector2(36f, 32f), away, CollisionFilter.Everything, out _));
    }

    // Detects is the default filter, so a collider that detects nothing sweeps through everything;
    // the overload is how a caller asks a different question without changing the collider.
    [Fact]
    public void Cast_UsesTheCollidersOwnFilterUnlessGivenAnother()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Prober prober = new(new Vector2(24f, 8f), new Vector2(8f, 8f));
        scene.Add(prober);

        Assert.Equal(CollisionFilter.None, prober.Collider.Filter);
        Assert.False(prober.Collider.Cast(new Vector2(0f, 40f), out _));

        Assert.True(prober.Collider.Cast(new Vector2(0f, 40f), scene.Collision.CreateFilter("solid"), out ShapeCastHit2D hit));
        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
    }
}

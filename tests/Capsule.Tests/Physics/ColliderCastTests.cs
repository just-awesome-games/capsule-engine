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
        body.Mover.BlocksOn("solid");
        scene.Add(body);

        MoveResult2D result = body.Mover.Move(new Vector2(0f, 60f));

        Assert.True(result.BlockedY);
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
    public void Move_OnAColliderInNoScene_SaysSo()
    {
        Body body = new(Vector2.Zero);

        Assert.Throws<InvalidOperationException>(() => body.Mover.Move(Vector2.UnitX));
    }

    [Fact]
    public void ADetectedContact_DoesNotBlockAMoverThatDoesNotBlockOnItsLayer()
    {
        Scene scene = new();
        Body player = new(Vector2.Zero);
        Body enemy = new(new Vector2(10f, 0f));
        player.Collider.Layer = "player";
        player.Collider.SetFilter("enemy");
        player.Collider.ReportsContacts = true;
        player.Mover.BlocksOn("solid");
        enemy.Collider.Layer = "enemy";

        List<ColliderContact2D> entered = [];
        player.Collider.ContactEntered += entered.Add;

        scene.Add(player);
        scene.Add(enemy);
        using SceneSimulation simulation = new(scene);

        MoveResult2D result = player.Mover.Move(new Vector2(12f, 0f));
        simulation.Step(SceneFixtures.Step(0));

        Assert.False(result.BlockedX);
        Assert.Equal(new Vector2(12f, 0f), player.Position);
        Assert.Empty(player.Mover.MoveContacts.ToArray());

        ColliderContact2D contact = Assert.Single(entered);
        Assert.Same(enemy, contact.OtherEntity);
        Assert.Same(enemy.Collider, contact.OtherCollider);
        Assert.Null(contact.Tile);
        Assert.True(float.IsFinite(contact.Point.X));
        Assert.True(float.IsFinite(contact.Point.Y));
    }

    // The per-move filter is for the step, not for the mover: what it blocks on afterwards is
    // whatever BlocksOn last said.
    [Fact]
    public void Move_WithABlockingFilter_HonoursItOverBlocksOnAndLeavesTheStandingFilterAlone()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        Body body = new(new Vector2(8f, 8f));
        body.Mover.BlocksOn("solid");
        scene.Add(body);

        CollisionFilter standing = body.Mover.Filter;

        // Blocking on nothing for this call alone: the floor the mover normally stops on is not
        // there as far as this move is concerned.
        MoveResult2D through = body.Mover.Move(new Vector2(0f, 60f), CollisionFilter.None);

        Assert.False(through.BlockedY);
        Assert.Equal(68f, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.Equal(standing, body.Mover.Filter);

        // And the next plain move resolves against the standing filter again.
        body.Teleport(new Vector2(8f, 8f));
        Assert.True(body.Mover.Move(new Vector2(0f, 60f)).BlockedY);
    }

    [Fact]
    public void Move_WithABlockingFilterFromAnotherWorld_IsRefused()
    {
        Scene scene = SceneFixtures.Terrain("....", "####");
        Body body = new(new Vector2(8f, 8f));
        scene.Add(body);

        CollisionWorld2D elsewhere = new Scene().Collision;
        CollisionFilter foreign = CollisionFilter.Of(elsewhere.Layer("solid"));

        Assert.Throws<ArgumentException>(() => body.Mover.Move(Vector2.UnitY, foreign));
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

using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

// Slope tiles, the slide a move makes along them, and the grounded body that walks them.
public sealed class SlopeTests
{
    private const float Speed = 2f;

    // A floating move into a slope keeps what the slope does not take. The box's bottom-right corner
    // meets the diagonal x + y = 64 after 4 units, and the other 16 slide up it as (8, -8).
    [Fact]
    public void MoveBox_IntoADiagonalWall_SlidesAlongItInsteadOfStopping()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "../#", "####");

        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(20f, 24f - CollisionTolerance.LinearSlop, 8f, 8f),
            new Vector2(20f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.True(result.Blocked);
        Assert.Equal(12f, result.Translation.X, 0.05f);
        Assert.Equal(-8f, result.Translation.Y, 0.05f);
    }

    // The slope's right side and the box's left side cover each other fully, so neither is a surface,
    // and the ground under the slope culls its bottom.
    // A filter that cannot see the box turns it back into empty space, and the slope's side is live
    // again.
    [Fact]
    public void ASlopeBesideASolidBox_SharesNoLiveEdgeWithIt()
    {
        CollisionWorld2D world = new();
        GridCollider2D grid = CollisionFixtures.Paint(world, "/=", "##");

        Assert.Equal(CellState2D.Edges | GridCollider2D.EdgeBit(0), grid.StateAt(0, 0));
        Assert.Equal(CellState2D.None, grid.StateAt(1, 0) & CellState2D.FaceMinX);

        Assert.True(world.Raycast(
            new Vector2(24f, 12f),
            -Vector2.UnitX,
            16f,
            world.CreateFilter(CollisionFixtures.Solid),
            out RayHit2D hit));
        Assert.Equal((0, 0), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(new Vector2(1f, 0f), hit.Normal);
        Assert.Equal(8f, hit.Distance, 3);
    }

    // Up a two-tile slope, across the top and down the far side. Every step ends on a floor, and a step
    // that stays on one surface covers its whole length along it.
    [Fact]
    public void AGroundedBody_WalksOverAHill_OnTheFloorEveryStep_AtOneSpeedAlongTheSurface()
    {
        Scene scene = SceneFixtures.Terrain(
            "..............",
            "..............",
            "..../#\\.......",
            ".../###\\......",
            "##############");
        SceneFixtures.Body body = Grounded(scene, new Vector2(8f, 40f));

        for (int step = 0; step < 90; step++)
        {
            Vector2 floor = body.Mover.FloorNormal;
            MoveResult2D result = body.Mover.Move(new Vector2(Speed, 0.5f));

            Assert.True(body.Mover.IsOnFloor, $"step {step} left the floor at {body.Position}");
            if (body.Mover.FloorNormal == floor)
            {
                Assert.Equal(Speed, result.Translation.Length(), 0.02f);
            }
        }

        Assert.True(body.Position.X > 128f);
        Assert.Equal(56f, body.Position.Y, CollisionFixtures.Tolerance);
    }

    // A floor stops a fall outright, so gravity alone never walks a body down a slope.
    [Fact]
    public void AGroundedBodyStandingOnASlope_DoesNotDrift()
    {
        Scene scene = SceneFixtures.Terrain("....", "./..", "####");
        SceneFixtures.Body body = Grounded(scene, new Vector2(20f, 0f));
        Vector2 landed = body.Position;
        Assert.True(body.Mover.FloorNormal.X < 0f);

        for (int step = 0; step < 120; step++)
        {
            body.Mover.Move(new Vector2(0f, 0.5f));
            Assert.True(body.Mover.IsOnFloor);
        }

        Assert.Equal(landed, body.Position);
    }

    // Lands a grounded 8x8 body from where it starts.
    private static SceneFixtures.Body Grounded(Scene scene, Vector2 position)
    {
        SceneFixtures.Body body = new(position, blocksOn: "solid");
        body.Mover.Mode = BodyMode.Grounded;
        scene.Add(body);
        body.Mover.Move(new Vector2(0f, 40f));
        Assert.True(body.Mover.IsOnFloor);

        return body;
    }
}

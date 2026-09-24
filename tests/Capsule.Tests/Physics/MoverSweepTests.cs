using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

public sealed class MoverSweepTests
{
    private const float Tolerance = CollisionFixtures.Tolerance;

    [Fact]
    public void MoveBox_StopsAtTheSurfaceItRunsIntoAndReportsIt()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "..#", "..#");

        Span<Contact2D> contacts = stackalloc Contact2D[8];
        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(0f, 4f, 8f, 8f),
            new Vector2(60f, 0f),
            CollisionFilter.Everything,
            contacts);

        // The wall's left face is at x = 32, so an 8-wide box starting at 0 may travel 24.
        Assert.Equal(24f, result.Translation.X, Tolerance);
        Assert.True(result.Blocked);
        Assert.Equal(1, result.ContactCount);
        Assert.Equal(new Vector2(-1f, 0f), contacts[0].Normal);
        Assert.Equal((2, 0), (contacts[0].Target.CellX, contacts[0].Target.CellY));
    }

    // Meeting a wall must not stop the fall, or a box pressed against a wall hangs there.
    [Fact]
    public void MoveBox_SlidesAlongASurfaceInsteadOfStoppingDead()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "..#", "..#", "..#");

        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(20f, 4f, 8f, 8f),
            new Vector2(20f, 10f),
            CollisionFilter.Everything,
            default);

        Assert.Equal(4f, result.Translation.X, Tolerance);
        Assert.Equal(10f, result.Translation.Y, Tolerance);
        Assert.True(result.Blocked);
    }

    // The seam between two tiles of one flat run is not a face and must never catch a box.
    [Fact]
    public void MoveBox_CrossesEveryCellSeamOfAFlatRunWithoutCatching()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "........", "########");

        Aabb2D box = CollisionFixtures.Box(0f, 4f, 8f, 8f);
        float travelled = 0f;

        for (int step = 0; step < 60; step++)
        {
            // Pressed down into the ground every step, exactly as a falling character is.
            MoveResult2D result = world.MoveBox(box, new Vector2(2f, 4f), CollisionFilter.Everything, default);
            box = box.Translated(result.Translation);
            travelled += result.Translation.X;
        }

        Assert.Equal(120f, travelled, Tolerance);
        Assert.Equal(8f, box.Min.Y, Tolerance);
    }

    // A leading corner landing exactly on a seam meets the surface and not the seam, then slides along
    // the surface for the rest of the move.
    [Fact]
    public void MoveBox_DrivingItsLeadingCornerIntoACellSeam_LandsOnTheSurface()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "####");

        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 8f, 8f),
            new Vector2(16f, 16f),
            CollisionFilter.Everything,
            default);

        Assert.Equal(16f, result.Translation.X, Tolerance);
        Assert.Equal(8f, result.Translation.Y, Tolerance);
        Assert.True(result.Blocked);
    }

    [Fact]
    public void MoveBox_RestsOnASurfaceWithoutDriftingOrJittering()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Aabb2D box = CollisionFixtures.Box(8f, 0f, 8f, 8f);
        float settled = float.NaN;

        for (int step = 0; step < 120; step++)
        {
            MoveResult2D result = world.MoveBox(box, new Vector2(0f, 5f), CollisionFilter.Everything, default);
            box = box.Translated(result.Translation);

            if (step == 8)
            {
                settled = box.Min.Y;
            }
        }

        Assert.Equal(8f, settled, Tolerance);
        Assert.Equal(settled, box.Min.Y);
    }

    [Fact]
    public void MoveBox_NeverPassesThroughAWallAtAnySpeed()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "..#", "..#");

        foreach (float speed in new[] { 40f, 400f, 40_000f, 4_000_000f })
        {
            MoveResult2D result = world.MoveBox(
                CollisionFixtures.Box(0f, 4f, 8f, 8f),
                new Vector2(speed, 0f),
                CollisionFilter.Everything,
                default);

            Assert.Equal(24f, result.Translation.X, Tolerance);
        }
    }

    // The mover insets a box on the axis it is not travelling along. A box thinner than twice the
    // point tolerance has no room for one, and the inset clamps to zero rather than inverting it.
    [Fact]
    public void MoveBox_ThinnerThanTwiceThePointTolerance_StillSweeps()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "####");

        MoveResult2D result = world.MoveBox(
            new Aabb2D(Vector2.Zero, new Vector2(4f, 0.008f)),
            new Vector2(10f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.False(result.Blocked);
        Assert.Equal(new Vector2(10f, 0f), result.Translation);
    }
}

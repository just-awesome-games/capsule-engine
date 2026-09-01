using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

public sealed class MoverTests
{
    private const float Tolerance = 2f * CollisionWorld.LinearSlop;

    [Fact]
    public void MoveBox_AppliesTheWholeTranslationWhereNothingIsInTheWay()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "....");

        MoveResult result = world.MoveBox(
            CollisionFixtures.Box(4f, 4f, 8f, 8f),
            new Vector2(10f, 6f),
            CollisionFilter.Everything,
            default);

        Assert.Equal(new Vector2(10f, 6f), result.Translation);
        Assert.False(result.BlockedX);
        Assert.False(result.BlockedY);
    }

    [Fact]
    public void MoveBox_StopsAtTheSurfaceItRunsIntoAndReportsIt()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "..#", "..#");

        Span<Contact> contacts = stackalloc Contact[8];
        MoveResult result = world.MoveBox(
            CollisionFixtures.Box(0f, 4f, 8f, 8f),
            new Vector2(60f, 0f),
            CollisionFilter.Everything,
            contacts);

        // The wall's left face is at x = 32, so an 8-wide box starting at 0 may travel 24.
        Assert.Equal(24f, result.Translation.X, Tolerance);
        Assert.True(result.BlockedX);
        Assert.Equal(1, result.ContactCount);
        Assert.Equal(new Vector2(-1f, 0f), contacts[0].Normal);
        Assert.Equal((2, 0), (contacts[0].Target.CellX, contacts[0].Target.CellY));
    }

    // Axis separation is the whole point of the mover: stopping on X must not stop Y, or a box
    // pressed against a wall stops falling.
    [Fact]
    public void MoveBox_SlidesAlongASurfaceInsteadOfStoppingDead()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "..#", "..#", "..#");

        MoveResult result = world.MoveBox(
            CollisionFixtures.Box(20f, 4f, 8f, 8f),
            new Vector2(20f, 10f),
            CollisionFilter.Everything,
            default);

        Assert.Equal(4f, result.Translation.X, Tolerance);
        Assert.Equal(10f, result.Translation.Y, Tolerance);
        Assert.True(result.BlockedX);
        Assert.False(result.BlockedY);
    }

    // The seam between two tiles of one flat run is not a face, and a box travelling along it must
    // never catch on one.
    [Fact]
    public void MoveBox_CrossesEveryCellSeamOfAFlatRunWithoutCatching()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "........", "########");

        Aabb box = CollisionFixtures.Box(0f, 4f, 8f, 8f);
        float travelled = 0f;

        for (int step = 0; step < 60; step++)
        {
            // Pressed down into the ground every step, exactly as a falling character is.
            MoveResult result = world.MoveBox(box, new Vector2(2f, 4f), CollisionFilter.Everything, default);
            box = box.Translated(result.Translation);
            travelled += result.Translation.X;
        }

        Assert.Equal(120f, travelled, Tolerance);
        Assert.Equal(8f, box.Min.Y, Tolerance);
    }

    // The mover separates the axes, so each sweep can only ever meet a face along the one it is
    // travelling; a leading corner landing exactly on a seam has no tie to resolve.
    [Fact]
    public void MoveBox_DrivingItsLeadingCornerIntoACellSeam_LandsOnTheSurface()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "####");

        MoveResult result = world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 8f, 8f),
            new Vector2(16f, 16f),
            CollisionFilter.Everything,
            default);

        Assert.Equal(16f, result.Translation.X, Tolerance);
        Assert.Equal(8f, result.Translation.Y, Tolerance);
        Assert.True(result.BlockedY);
        Assert.False(result.BlockedX);
    }

    [Fact]
    public void MoveBox_RestsOnASurfaceWithoutDriftingOrJittering()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Aabb box = CollisionFixtures.Box(8f, 0f, 8f, 8f);
        float settled = float.NaN;

        for (int step = 0; step < 120; step++)
        {
            MoveResult result = world.MoveBox(box, new Vector2(0f, 5f), CollisionFilter.Everything, default);
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
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "..#", "..#");

        foreach (float speed in new[] { 40f, 400f, 40_000f, 4_000_000f })
        {
            MoveResult result = world.MoveBox(
                CollisionFixtures.Box(0f, 4f, 8f, 8f),
                new Vector2(speed, 0f),
                CollisionFilter.Everything,
                default);

            Assert.Equal(24f, result.Translation.X, Tolerance);
        }
    }

    [Fact]
    public void MoveBox_LandsOnAOneWayEdgeFromAboveAndPassesItFromBelow()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "----", "....");

        MoveResult falling = world.MoveBox(
            CollisionFixtures.Box(20f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default);
        Assert.True(falling.BlockedY);
        Assert.Equal(4f, falling.Translation.Y, Tolerance);

        MoveResult rising = world.MoveBox(
            CollisionFixtures.Box(20f, 36f, 8f, 8f),
            new Vector2(0f, -20f),
            CollisionFilter.Everything,
            default);
        Assert.False(rising.BlockedY);
        Assert.Equal(-20f, rising.Translation.Y, Tolerance);
    }

    [Fact]
    public void MoveBox_IsNeverStoppedSidewaysByAOneWayEdge()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "----", "----");

        MoveResult sideways = world.MoveBox(
            CollisionFixtures.Box(0f, 12f, 8f, 8f),
            new Vector2(40f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.False(sideways.BlockedX);
        Assert.Equal(40f, sideways.Translation.X, Tolerance);
    }

    [Fact]
    public void MoveBox_FallsPastAOneWayEdgeItAlreadyStartedBelow()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "....", "----", "....");

        MoveResult result = world.MoveBox(
            CollisionFixtures.Box(20f, 20f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default);

        Assert.False(result.BlockedY);
        Assert.Equal(20f, result.Translation.Y, Tolerance);
    }

    [Fact]
    public void MoveBox_IsBlockedByAnotherColliderAndIgnoresItsOwn()
    {
        CollisionWorld world = new();
        CollisionTag body = world.Tag("body");
        ColliderHandle self = world.Add(Shape.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, body, CollisionFilter.None);
        world.Add(Shape.Box(new Vector2(40f, 0f), new Vector2(8f, 8f)), Vector2.Zero, body, CollisionFilter.None);

        MoveResult blocked = world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 8f, 8f),
            new Vector2(60f, 0f),
            CollisionFilter.Of(body),
            default,
            self);

        Assert.True(blocked.BlockedX);
        Assert.Equal(32f, blocked.Translation.X, Tolerance);

        MoveResult unfiltered = world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 8f, 8f),
            new Vector2(60f, 0f),
            CollisionFilter.None,
            default,
            self);

        Assert.False(unfiltered.BlockedX);
    }

    [Fact]
    public void MoveBox_IsStoppedByTheFaceASolidCellSharesWithOneTheFilterExcludes()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "=#..");

        MoveResult result = world.MoveBox(
            CollisionFixtures.Box(40f, 4f, 8f, 8f),
            new Vector2(-40f, 0f),
            world.Filter(CollisionFixtures.Climb),
            default);

        Assert.True(result.BlockedX);
        Assert.Equal(-24f, result.Translation.X, Tolerance);
    }

    [Fact]
    public void MoveBox_LeavesAnOverlapItAlreadyStartedInsteadOfFreezingInIt()
    {
        CollisionWorld world = new();
        CollisionFixtures.Paint(world, "..#", "..#");

        MoveResult escaping = world.MoveBox(
            CollisionFixtures.Box(34f, 4f, 8f, 8f),
            new Vector2(-20f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.Equal(-20f, escaping.Translation.X, Tolerance);
    }

    [Fact]
    public void MoveBox_RejectsATranslationThatIsNotFinite()
    {
        CollisionWorld world = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 8f, 8f),
            new Vector2(float.NaN, 0f),
            CollisionFilter.Everything,
            default));
    }

    // Handles and tags name a slot in one world and never compare across two, so what two runs owe
    // each other is the same surfaces in the same order — cells, tag names and normals.
    [Fact]
    public void MoveBox_ProducesTheSameContactsForTheSameInputsOnAFreshWorld()
    {
        static (Vector2 Translation, (bool Tile, int X, int Y, string Tag, Vector2 Normal)[] Contacts) Run()
        {
            CollisionWorld world = new();
            CollisionFixtures.Paint(world, "..#", "####");
            world.Add(
                Shape.Circle(new Vector2(20f, 6f), 3f),
                Vector2.Zero,
                world.Tag("pickup"),
                CollisionFilter.None);

            Contact[] contacts = new Contact[8];
            MoveResult result = world.MoveBox(
                CollisionFixtures.Box(0f, 8f, 8f, 8f),
                new Vector2(40f, 12f),
                CollisionFilter.Everything,
                contacts);

            return (
                result.Translation,
                [.. contacts[..result.ContactCount].Select(contact => (
                    contact.Target.IsGridCell,
                    contact.Target.CellX,
                    contact.Target.CellY,
                    world.NameOf(contact.Target.Tag),
                    contact.Normal))]);
        }

        (Vector2 first, (bool, int, int, string, Vector2)[] firstContacts) = Run();
        (Vector2 second, (bool, int, int, string, Vector2)[] secondContacts) = Run();

        Assert.Equal(first, second);
        Assert.Equal(firstContacts, secondContacts);
        Assert.NotEmpty(firstContacts);
    }
}

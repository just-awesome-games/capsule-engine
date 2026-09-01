using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

public sealed class MoverTests
{
    private const float Tolerance = 2f * CollisionWorld2D.LinearSlop;

    [Fact]
    public void MoveBox_AppliesTheWholeTranslationWhereNothingIsInTheWay()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "....");

        MoveResult2D result = world.MoveBox(
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
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "..#", "..#", "..#");

        MoveResult2D result = world.MoveBox(
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

    // The mover separates the axes, so each sweep can only ever meet a face along the one it is
    // travelling; a leading corner landing exactly on a seam has no tie to resolve.
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
        Assert.True(result.BlockedY);
        Assert.False(result.BlockedX);
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

    [Fact]
    public void MoveBox_LandsOnATopFaceFromAboveAndPassesItFromBelow()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "----", "....");

        MoveResult2D falling = world.MoveBox(
            CollisionFixtures.Box(20f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default);
        Assert.True(falling.BlockedY);
        Assert.Equal(4f, falling.Translation.Y, Tolerance);

        MoveResult2D rising = world.MoveBox(
            CollisionFixtures.Box(20f, 36f, 8f, 8f),
            new Vector2(0f, -20f),
            CollisionFilter.Everything,
            default);
        Assert.False(rising.BlockedY);
        Assert.Equal(-20f, rising.Translation.Y, Tolerance);
    }

    [Fact]
    public void MoveBox_IsNeverStoppedSidewaysByATopFace()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "----", "----");

        MoveResult2D sideways = world.MoveBox(
            CollisionFixtures.Box(0f, 12f, 8f, 8f),
            new Vector2(40f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.False(sideways.BlockedX);
        Assert.Equal(40f, sideways.Translation.X, Tolerance);
    }

    [Fact]
    public void MoveBox_FallsPastATopFaceItAlreadyStartedBelow()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "----", "....");

        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(20f, 20f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default);

        Assert.False(result.BlockedY);
        Assert.Equal(20f, result.Translation.Y, Tolerance);
    }

    // A face is a declared plane, so the surface it reports is its own normal whatever the
    // narrowphase measured against. A rounded shape coming down past the near end of an edge is
    // nearest to that endpoint, and GJK answers with the diagonal from the corner — a direction the
    // surface does not have, and one a game classifying grounded-versus-wall by normal would read
    // as a wall. The shape is placed so its centre is off the end of the face and only its side
    // overhangs, which is exactly where the endpoint is the nearest feature.
    [Theory]
    [InlineData(ShapeKind2D.Circle)]
    [InlineData(ShapeKind2D.Capsule)]
    [InlineData(ShapeKind2D.Box)]
    public void ShapeCast_PastTheEndOfATopFace_ReportsTheFacesOwnNormal(ShapeKind2D kind)
    {
        CollisionWorld2D world = new();

        // The only collidable cell is (1, 1), so the face spans x = 16..32 at y = 16.
        CollisionFixtures.Paint(world, "..", ".-");

        Assert.True(world.ShapeCast(
            Landing(kind),
            new Vector2(14f, 4f),
            new Vector2(0f, 24f),
            CollisionFilter.Everything,
            out ShapeCastHit2D hit));

        Assert.True(hit.Target.IsGridCell);
        Assert.Equal((1, 1), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
    }

    // The same claim through the mover, which is what a game actually reads: every contact it
    // writes for a directional face carries that face's normal.
    [Theory]
    [InlineData(ShapeKind2D.Circle)]
    [InlineData(ShapeKind2D.Capsule)]
    public void Move_PastTheEndOfATopFace_ReportsTheFacesOwnNormal(ShapeKind2D kind)
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "..", ".-");

        Contact2D[] contacts = new Contact2D[8];
        MoveResult2D result = world.Move(
            Landing(kind),
            new Vector2(14f, 4f),
            new Vector2(0f, 24f),
            CollisionFilter.Everything,
            contacts);

        Assert.True(result.BlockedY);
        Assert.NotEqual(0, result.ContactCount);
        Assert.All(
            contacts[..result.ContactCount].ToArray(),
            contact => Assert.Equal(new Vector2(0f, -1f), contact.Normal));
    }

    // Centred on the origin, so the cast origin places the shape's middle: a box takes the
    // closed-form sweep and is the control, the rounded pair take the GJK path this is about.
    private static Shape2D Landing(ShapeKind2D kind) => kind switch
    {
        ShapeKind2D.Circle => Shape2D.Circle(Vector2.Zero, 4f),
        ShapeKind2D.Capsule => Shape2D.Capsule(new Vector2(0f, -4f), new Vector2(0f, 4f), 3f),
        _ => Shape2D.Box(Aabb2D.FromCenter(Vector2.Zero, new Vector2(8f, 8f))),
    };

    // The generalisation: a face is a direction, so the same primitive pointed the other way is
    // what a body under reversed gravity stands on.
    [Fact]
    public void MoveBox_LandsOnABottomFaceFromBelowAndPassesItFromAbove()
    {
        CollisionWorld2D world = new();
        world.AddGrid(
            CollisionFixtures.TileSize,
            1,
            3,
            [0, 1, 0],
            [new CellProfile2D(null), new CellProfile2D(world.Layer("ceiling"), CellFaces2D.Bottom)]);

        // The face is at y = 32; a box rising from below stops with its top there.
        MoveResult2D rising = world.MoveBox(
            CollisionFixtures.Box(4f, 44f, 8f, 8f),
            new Vector2(0f, -20f),
            CollisionFilter.Everything,
            default);
        Assert.True(rising.BlockedY);
        Assert.Equal(-12f, rising.Translation.Y, Tolerance);

        MoveResult2D falling = world.MoveBox(
            CollisionFixtures.Box(4f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default);
        Assert.False(falling.BlockedY);
        Assert.Equal(20f, falling.Translation.Y, Tolerance);
    }

    [Fact]
    public void MoveBox_IsBlockedByAnotherColliderAndIgnoresItsOwn()
    {
        CollisionWorld2D world = new();
        CollisionLayer body = world.Layer("body");
        ColliderHandle self = world.Add(Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)), Vector2.Zero, body, CollisionFilter.None);
        world.Add(Shape2D.Box(new Vector2(40f, 0f), new Vector2(8f, 8f)), Vector2.Zero, body, CollisionFilter.None);

        MoveResult2D blocked = world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 8f, 8f),
            new Vector2(60f, 0f),
            CollisionFilter.Of(body),
            default,
            self);

        Assert.True(blocked.BlockedX);
        Assert.Equal(32f, blocked.Translation.X, Tolerance);

        MoveResult2D unfiltered = world.MoveBox(
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
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "=#..");

        MoveResult2D result = world.MoveBox(
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
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "..#", "..#");

        MoveResult2D escaping = world.MoveBox(
            CollisionFixtures.Box(34f, 4f, 8f, 8f),
            new Vector2(-20f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.Equal(-20f, escaping.Translation.X, Tolerance);
    }

    [Fact]
    public void MoveBox_RejectsATranslationThatIsNotFinite()
    {
        CollisionWorld2D world = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => world.MoveBox(
            CollisionFixtures.Box(0f, 0f, 8f, 8f),
            new Vector2(float.NaN, 0f),
            CollisionFilter.Everything,
            default));
    }

    // Handles and layers name a slot in one world and never compare across two, so what two runs
    // owe each other is the same surfaces in the same order — cells, layer names and normals.
    [Fact]
    public void MoveBox_ProducesTheSameContactsForTheSameInputsOnAFreshWorld()
    {
        static (Vector2 Translation, (bool Tile, int X, int Y, string Layer, Vector2 Normal)[] Contacts) Run()
        {
            CollisionWorld2D world = new();
            CollisionFixtures.Paint(world, "..#", "####");
            world.Add(
                Shape2D.Circle(new Vector2(20f, 6f), 3f),
                Vector2.Zero,
                world.Layer("pickup"),
                CollisionFilter.None);

            Contact2D[] contacts = new Contact2D[8];
            MoveResult2D result = world.MoveBox(
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
                    world.NameOf(contact.Target.Layer),
                    contact.Normal))]);
        }

        (Vector2 first, (bool, int, int, string, Vector2)[] firstContacts) = Run();
        (Vector2 second, (bool, int, int, string, Vector2)[] secondContacts) = Run();

        Assert.Equal(first, second);
        Assert.Equal(firstContacts, secondContacts);
        Assert.NotEmpty(firstContacts);
    }

}

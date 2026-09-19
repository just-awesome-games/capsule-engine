using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

public sealed class MoverFaceTests
{
    private const float Tolerance = CollisionFixtures.Tolerance;

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

    // A face reports its own normal whatever the narrowphase measured. A rounded shape coming down
    // past the near end of an edge is nearest that endpoint, where GJK answers with the diagonal
    // from the corner — a direction the surface does not have. Each shape is placed with its centre
    // off the end of the face, which is where the endpoint is the nearest feature.
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

    // The same claim through the mover: every contact it writes for a directional face carries
    // that face's normal.
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

    // Centred on the origin, so the cast origin places the shape's middle. The box is the control:
    // it takes the closed-form sweep, the rounded pair take the GJK path this is about.
    private static Shape2D Landing(ShapeKind2D kind) => kind switch
    {
        ShapeKind2D.Circle => Shape2D.Circle(Vector2.Zero, 4f),
        ShapeKind2D.Capsule => Shape2D.Capsule(new Vector2(0f, -4f), new Vector2(0f, 4f), 3f),
        _ => Shape2D.Box(Aabb2D.FromCenter(Vector2.Zero, new Vector2(8f, 8f))),
    };

    // A face is a direction, so the same primitive pointed the other way is what a body under
    // reversed gravity stands on. The middle cell's Top plane is y = 16 and its Bottom plane
    // y = 32, so a landing move gives up whatever of its 20 units of travel the plane takes.
    [Theory]
    [InlineData(CellFaces2D.Top, 4f, 20f, 4f, 36f, -20f)]
    [InlineData(CellFaces2D.Bottom, 44f, -20f, -12f, 4f, 20f)]
    public void MoveBox_LandsOnAFaceFromItsOutwardSideAndPassesItFromTheOther(
        CellFaces2D face,
        float landingFrom,
        float onto,
        float landedBy,
        float passingFrom,
        float through)
    {
        CollisionWorld2D world = CollisionFixtures.OneFace(face);

        MoveResult2D landing = world.MoveBox(
            CollisionFixtures.Box(20f, landingFrom, 8f, 8f),
            new Vector2(0f, onto),
            CollisionFilter.Everything,
            default);
        Assert.True(landing.BlockedY);
        Assert.Equal(landedBy, landing.Translation.Y, Tolerance);

        MoveResult2D passing = world.MoveBox(
            CollisionFixtures.Box(20f, passingFrom, 8f, 8f),
            new Vector2(0f, through),
            CollisionFilter.Everything,
            default);
        Assert.False(passing.BlockedY);
        Assert.Equal(through, passing.Translation.Y, Tolerance);
    }

    [Fact]
    public void MoveBox_IsStoppedByTheFaceASolidCellSharesWithOneTheFilterExcludes()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "=#..");

        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(40f, 4f, 8f, 8f),
            new Vector2(-40f, 0f),
            world.CreateFilter(CollisionFixtures.Climb),
            default);

        Assert.True(result.BlockedX);
        Assert.Equal(-24f, result.Translation.X, Tolerance);
    }
}

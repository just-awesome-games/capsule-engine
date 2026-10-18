using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

public sealed class MoverFaceTests
{
    private const float Tolerance = CollisionFixtures.Tolerance;

    [Fact]
    public void MoveBox_IsNeverStoppedSidewaysByAOneWayEdge()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "----", "----");

        MoveResult2D sideways = world.MoveBox(
            CollisionFixtures.Box(0f, 12f, 8f, 8f),
            new Vector2(40f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.False(sideways.Blocked);
        Assert.Equal(40f, sideways.Translation.X, Tolerance);
    }

    [Fact]
    public void MoveBox_FallsPastAOneWayEdgeItAlreadyStartedBelow()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "----", "....");

        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(20f, 20f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default);

        Assert.False(result.Blocked);
        Assert.Equal(20f, result.Translation.Y, Tolerance);
    }

    // A face reports its own normal whatever the narrowphase measured. A rounded shape coming down
    // past the near end of an edge, or resting there, is nearest that endpoint. There GJK answers with
    // the diagonal from the corner, a direction the surface does not have. Each shape is placed with
    // its centre off the end of the face, which is where the endpoint is the nearest feature.
    [Theory]
    [InlineData(ShapeKind2D.Circle, false)]
    [InlineData(ShapeKind2D.Capsule, false)]
    [InlineData(ShapeKind2D.Box, false)]
    [InlineData(ShapeKind2D.Circle, true)]
    public void ACastOrOverlapPastTheEndOfAOneWayEdge_ReportsTheEdgesOwnNormal(ShapeKind2D kind, bool overlap)
    {
        CollisionWorld2D world = new();

        // The only collidable cell is (1, 1). The face spans x = 16..32 at y = 16.
        CollisionFixtures.Paint(world, "..", ".-");

        (CollisionTarget target, Vector2 normal) = overlap ? Overlap() : Cast();

        Assert.True(target.IsGridCell);
        Assert.Equal((1, 1), (target.CellX, target.CellY));
        Assert.Equal(new Vector2(0f, -1f), normal);

        (CollisionTarget, Vector2) Cast()
        {
            Assert.True(world.ShapeCast(Landing(kind), new Vector2(14f, 4f), new Vector2(0f, 24f), CollisionFilter.Everything, out ShapeCastHit2D hit));

            return (hit.Target, hit.Normal);
        }

        // Close enough to the endpoint (16, 16) to be inside the skin.
        (CollisionTarget, Vector2) Overlap()
        {
            ColliderHandle resting = world.Add(Landing(kind), new Vector2(14f, 12.52f), world.Layer("body"));
            Contact2D[] contacts = new Contact2D[8];
            Assert.Equal(1, world.OverlapColliderAll(resting, CollisionFilter.Everything, contacts));

            return (contacts[0].Target, contacts[0].Normal);
        }
    }

    // Centred on the origin. The cast origin places the shape's middle. The box is the control:
    // it takes the closed-form sweep, the rounded pair take the GJK path this is about.
    private static Shape2D Landing(ShapeKind2D kind) => kind switch
    {
        ShapeKind2D.Circle => Shape2D.Circle(Vector2.Zero, 4f),
        ShapeKind2D.Capsule => Shape2D.Capsule(new Vector2(0f, -4f), new Vector2(0f, 4f), 3f),
        _ => Shape2D.Box(Aabb2D.FromCenter(Vector2.Zero, new Vector2(8f, 8f))),
    };
}

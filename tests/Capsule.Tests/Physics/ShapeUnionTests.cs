using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

/// <summary>
/// The union's completeness rule: every shape the public API can build answers every verb the
/// query seam offers. A shape that exists and throws on use is the failure this guards against.
/// </summary>
public sealed class ShapeUnionTests
{
    // The one union member ShapeKind2D does not name: a polygon with a radius.
    private const string RoundedPolygon = nameof(RoundedPolygon);

    public static TheoryData<string> Union =>
        [nameof(ShapeKind2D.Circle), nameof(ShapeKind2D.Capsule), nameof(ShapeKind2D.Box), nameof(ShapeKind2D.Polygon), RoundedPolygon];

    [Theory]
    [MemberData(nameof(Union))]
    public void EveryShape_IsFoundByAnOverlapThatCoversIt(string kind)
    {
        CollisionWorld2D world = new();
        CollisionLayer target = world.Layer("target");
        world.Add(Of(kind), new Vector2(50f, 50f), target);

        Span<Contact2D> contacts = stackalloc Contact2D[4];

        Assert.Equal(1, world.OverlapBoxAll(
            Aabb2D.FromCorner(new Vector2(30f, 30f), new Vector2(40f, 40f)),
            CollisionFilter.Everything,
            contacts));
        Assert.Equal(target, contacts[0].Target.Layer);
    }

    // The ray runs through the shape's centre, where its leftmost point is its bounds' left edge.
    [Theory]
    [MemberData(nameof(Union))]
    public void EveryShape_StopsARayAtItsNearSide(string kind)
    {
        CollisionWorld2D world = new();
        Shape2D shape = Of(kind);
        world.Add(shape, new Vector2(50f, 50f), world.Layer("target"));

        Assert.True(world.Raycast(new Vector2(0f, 50f), Vector2.UnitX, 100f, CollisionFilter.Everything, out RayHit2D hit));
        Assert.Equal(50f + shape.Bounds.Min.X, hit.Distance, 3);
    }

    [Theory]
    [MemberData(nameof(Union))]
    public void EveryShape_StopsAShapeCastAndAMoveSweptIntoIt(string kind)
    {
        CollisionWorld2D world = new();
        world.Add(Of(kind), new Vector2(50f, 50f), world.Layer("target"));

        Assert.True(world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)),
            new Vector2(0f, 46f),
            new Vector2(100f, 0f),
            CollisionFilter.Everything,
            out ShapeCastHit2D hit));
        Assert.InRange(hit.Fraction, 0.01f, 0.99f);

        MoveResult2D moved = world.MoveBox(
            Aabb2D.FromCorner(new Vector2(0f, 46f), new Vector2(8f, 8f)),
            new Vector2(100f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.True(moved.Blocked);
        Assert.InRange(moved.Translation.X, 1f, 99f);
    }

    private static Shape2D Of(string kind) => kind switch
    {
        nameof(ShapeKind2D.Circle) => Shape2D.Circle(Vector2.Zero, 10f),
        nameof(ShapeKind2D.Capsule) => Shape2D.Capsule(new Vector2(0f, -8f), new Vector2(0f, 8f), 6f),
        nameof(ShapeKind2D.Box) => Shape2D.Box(new Vector2(-10f, -10f), new Vector2(20f, 20f)),
        nameof(ShapeKind2D.Polygon) => Shape2D.Polygon(
            [new Vector2(-10f, 0f), new Vector2(0f, -10f), new Vector2(10f, 0f), new Vector2(0f, 10f)]),
        RoundedPolygon => Shape2D.Polygon(
            [new Vector2(-8f, 0f), new Vector2(0f, -8f), new Vector2(8f, 0f), new Vector2(0f, 8f)],
            3f),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind names no shape in Union. Add the shape to Union and to Of together."),
    };
}

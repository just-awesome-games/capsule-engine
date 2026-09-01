using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

/// <summary>
/// The union's completeness rule: every shape the public API can build answers every verb the
/// query seam offers. A shape that exists and throws on use is the failure this guards against.
/// </summary>
public sealed class ShapeUnionTests
{
    public static TheoryData<string> Union =>
        [nameof(ShapeKind.Circle), nameof(ShapeKind.Capsule), nameof(ShapeKind.Box), nameof(ShapeKind.Polygon), "RoundedPolygon"];

    [Theory]
    [MemberData(nameof(Union))]
    public void EveryShape_IsFoundByAnOverlapThatCoversIt(string kind)
    {
        CollisionWorld world = new();
        CollisionTag target = world.Tag("target");
        world.Add(Of(kind), new Vector2(50f, 50f), target, CollisionFilter.None);

        Span<Contact> contacts = stackalloc Contact[4];

        Assert.Equal(1, world.OverlapBox(
            Aabb.FromCorner(new Vector2(30f, 30f), new Vector2(40f, 40f)),
            CollisionFilter.Everything,
            contacts));
        Assert.Equal(target, contacts[0].Target.Tag);
    }

    [Theory]
    [MemberData(nameof(Union))]
    public void EveryShape_IsHitByARayAimedAtIt(string kind)
    {
        CollisionWorld world = new();
        world.Add(Of(kind), new Vector2(50f, 50f), world.Tag("target"), CollisionFilter.None);

        Assert.True(world.Raycast(
            new Vector2(0f, 50f),
            Vector2.UnitX,
            200f,
            CollisionFilter.Everything,
            out RayHit hit));
        Assert.InRange(hit.Distance, 1f, 50f);
        Assert.True(Vector2.Dot(hit.Normal, Vector2.UnitX) < 0f);
    }

    [Theory]
    [MemberData(nameof(Union))]
    public void EveryShape_StopsAShapeCastAndAMoveSweptIntoIt(string kind)
    {
        CollisionWorld world = new();
        world.Add(Of(kind), new Vector2(50f, 50f), world.Tag("target"), CollisionFilter.None);

        Assert.True(world.ShapeCast(
            Shape.Box(Vector2.Zero, new Vector2(8f, 8f)),
            new Vector2(0f, 46f),
            new Vector2(100f, 0f),
            CollisionFilter.Everything,
            out ShapeCastHit hit));
        Assert.InRange(hit.Fraction, 0.01f, 0.99f);

        MoveResult moved = world.MoveBox(
            Aabb.FromCorner(new Vector2(0f, 46f), new Vector2(8f, 8f)),
            new Vector2(100f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.True(moved.BlockedX);
        Assert.InRange(moved.Translation.X, 1f, 99f);
    }

    [Theory]
    [MemberData(nameof(Union))]
    public void EveryShape_MovesAsAColliderOfItsOwn(string kind)
    {
        CollisionWorld world = new();
        CollisionTag wall = world.Tag("wall");
        world.Add(Shape.Box(Vector2.Zero, new Vector2(16f, 64f)), new Vector2(100f, 20f), wall, CollisionFilter.None);

        ColliderHandle mover = world.Add(Of(kind), new Vector2(20f, 50f), world.Tag("mover"), CollisionFilter.Of(wall));

        Span<Contact> contacts = stackalloc Contact[4];
        MoveResult result = world.MoveCollider(mover, new Vector2(200f, 0f), contacts);

        Assert.True(result.BlockedX);
        Assert.InRange(result.Translation.X, 1f, 199f);
        Assert.Equal(new Vector2(20f, 50f) + result.Translation, world.PositionOf(mover));
    }

    private static Shape Of(string kind) => kind switch
    {
        nameof(ShapeKind.Circle) => Shape.Circle(Vector2.Zero, 10f),
        nameof(ShapeKind.Capsule) => Shape.Capsule(new Vector2(0f, -8f), new Vector2(0f, 8f), 6f),
        nameof(ShapeKind.Box) => Shape.Box(new Vector2(-10f, -10f), new Vector2(20f, 20f)),
        nameof(ShapeKind.Polygon) => Shape.Polygon(
            [new Vector2(-10f, 0f), new Vector2(0f, -10f), new Vector2(10f, 0f), new Vector2(0f, 10f)]),
        _ => Shape.Polygon(
            [new Vector2(-8f, 0f), new Vector2(0f, -8f), new Vector2(8f, 0f), new Vector2(0f, 8f)],
            3f),
    };
}

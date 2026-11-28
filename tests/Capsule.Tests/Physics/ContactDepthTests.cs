using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

public sealed class ContactDepthTests
{
    private const float Slack = 0.01f;

    private static readonly Vector2[] Diamond = [new(0f, -8f), new(8f, 0f), new(0f, 8f), new(-8f, 0f)];

    public static TheoryData<string> Pairs => ["polygons", "circle in box", "box in box", "box in cell"];

    // A contact's depth and normal are the shortest way out. Moving the probe by Normal * Depth leaves
    // it touching, a little less leaves it overlapping and a little more leaves it clear. One box overlaps
    // another by 3 across X, and a lone solid cell by 2 down Y.
    [Theory]
    [MemberData(nameof(Pairs))]
    public void AnOverlapsDepthAndNormal_LeadTheProbeOutToTouching(string pair)
    {
        CollisionWorld2D world = new();
        Vector2 square = new(8f, 8f);
        (Shape2D target, Shape2D probe, Vector2 origin, Vector2 normal, float depth) = pair switch
        {
            "polygons" => (Shape2D.Polygon(Diamond), Shape2D.Polygon(Diamond), new Vector2(10f, 2f), Vector2.Normalize(Vector2.One), 4f / MathF.Sqrt(2f)),
            "circle in box" => (Shape2D.Box(new Vector2(-8f, -8f), new Vector2(16f, 16f)), Shape2D.Circle(Vector2.Zero, 2f), new Vector2(5f, 1f), Vector2.UnitX, 5f),
            "box in box" => (Shape2D.Box(new Vector2(5f, 0f), square), Shape2D.Box(Vector2.Zero, square), Vector2.Zero, -Vector2.UnitX, 3f),
            _ => (default(Shape2D), Shape2D.Box(Vector2.Zero, square), new Vector2(20f, 10f), -Vector2.UnitY, 2f),
        };
        if (pair == "box in cell")
        {
            CollisionFixtures.Paint(world, "...", ".#.", "...");
        }
        else
        {
            world.Add(target, Vector2.Zero, world.Layer("item"));
        }

        Span<Contact2D> contacts = stackalloc Contact2D[1];

        Assert.Equal(1, world.OverlapAll(probe, origin, CollisionFilter.Everything, contacts));
        Assert.Equal(depth, contacts[0].Depth, 1e-4f);
        Assert.Equal(normal.X, contacts[0].Normal.X, 1e-4f);
        Assert.Equal(normal.Y, contacts[0].Normal.Y, 1e-4f);

        Vector2 way = contacts[0].Normal;
        Assert.Equal(1, world.OverlapAll(probe, origin + (way * (depth - Slack)), CollisionFilter.Everything, contacts));
        Assert.Equal(0, world.OverlapAll(probe, origin + (way * (depth + Slack)), CollisionFilter.Everything, contacts));
    }
}

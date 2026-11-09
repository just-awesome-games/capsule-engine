using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Holder = Capsule.Tests.Scenes.SceneFixtures.Holder;

namespace Capsule.Tests.Physics;

public sealed class PolygonColliderTests
{
    private const float Slop = CollisionTolerance.LinearSlop;

    // A beam from the entity's position, 48 units long and 16 across at its far end.
    private static readonly Vector2[] Open = [Vector2.Zero, new(48f, -8f), new(48f, 8f)];

    // New points sit at the entity's position plus the offset, rounded by the radius the collider was
    // built with, and the world, its queries and its contacts see them as soon as the call returns.
    [Fact]
    public void SetPoints_PlacesTheNewOutlineAsTheConstructorDoesAndQueriesSeeItAtOnce()
    {
        Scene scene = new();
        PolygonCollider2D beam = Beam(radius: 1f);
        beam.Offset = new Vector2(2f, 0f);
        List<string> log = Logged(beam);
        scene.Add(new Holder(new Vector2(100f, 50f), beam));
        BoxCollider2D post = new(new Vector2(4f, 4f));
        scene.Add(new Holder(new Vector2(102f, 85f), post));

        using SimulationHost run = new(scene);
        run.Step();
        Assert.Empty(log);

        beam.SetPoints([Vector2.Zero, new Vector2(-4f, 40f), new Vector2(4f, 40f)]);

        Assert.Equal(1f, beam.Radius);
        Assert.Equal(new Aabb2D(new Vector2(97f, 49f), new Vector2(107f, 91f)), beam.Bounds);
        Assert.Equal(beam.Bounds, scene.Collision.WorldShapeOf(beam.Handle).Bounds);
        Span<Contact2D> found = stackalloc Contact2D[4];
        Assert.Equal(1, beam.OverlapAll(found));
        Assert.Equal(post.Handle, found[0].Target.Collider);

        run.Step();
        Assert.Equal(["+"], log);
        Assert.Equal(post, Assert.Single(beam.Touching.ToArray()).OtherCollider);
    }

    // A beam closing to a line leaves the world with Enabled on, raising its exit as disabling does and
    // keeping its last open outline, and opening again rejoins with an enter. Every row is narrower than
    // twice the slop, including outlines Shape2D.Polygon refuses for coincident or collinear corners.
    [Theory]
    [InlineData("tips meet")]
    [InlineData("tips within the slop")]
    [InlineData("tips just past the slop")]
    [InlineData("a straight line")]
    [InlineData("every point at one place")]
    public void AThinOutline_CollapsesTheColliderUntilAnOutlineSpansAgain(string thin)
    {
        Vector2[] closed = thin switch
        {
            "tips meet" => [Vector2.Zero, new(48f, 0f), new(48f, 0f)],
            "tips within the slop" => [Vector2.Zero, new(48f, -0.4f * Slop), new(48f, 0.4f * Slop)],
            "tips just past the slop" => [Vector2.Zero, new(48f, -0.75f * Slop), new(48f, 0.75f * Slop)],
            "a straight line" => [Vector2.Zero, new(24f, 0f), new(48f, 0f)],
            _ => [Vector2.Zero, Vector2.Zero, Vector2.Zero],
        };

        Scene scene = new();
        PolygonCollider2D beam = Beam();
        List<string> log = Logged(beam);
        scene.Add(new Holder(Vector2.Zero, beam));
        scene.Add(new Holder(new Vector2(40f, -2f), new BoxCollider2D(new Vector2(4f, 4f))));
        using SimulationHost run = new(scene);
        run.Step();
        Assert.Equal(["+"], log);

        beam.SetPoints(closed);
        Assert.Equal(["+", "-"], log);
        Assert.True(beam.Enabled);
        Assert.Null(beam.World);
        Assert.Equal(1, scene.Collision.ColliderCount);
        Assert.Equal(new Aabb2D(new Vector2(0f, -8f), new Vector2(48f, 8f)), beam.Shape.Bounds);

        run.Step();
        beam.SetPoints(closed);
        run.Step();
        Assert.Equal(["+", "-"], log);

        beam.SetPoints([Vector2.Zero, new Vector2(48f, -2f), new Vector2(48f, 2f)]);
        Assert.NotNull(beam.World);
        run.Step();
        Assert.Equal(["+", "-", "+"], log);
        Assert.Equal(new Aabb2D(new Vector2(0f, -2f), new Vector2(48f, 2f)), beam.Shape.Bounds);
    }

    // Past four corners each distinct outline would be held for the life of the process. A collider
    // that changes its outline keeps to four. Any other refusal is the constructor's. A throw leaves the
    // collider as it was.
    [Theory]
    [InlineData("five corners", "keeps to 4 points")]
    [InlineData("two corners", "at least three corners")]
    [InlineData("a reflex corner", "collinear or reflex")]
    [InlineData("two corners at one place", "closer together than the linear slop")]
    [InlineData("a corner at infinity", "finite")]
    public void SetPoints_RefusesOutlinesItCannotHold(string refusal, string message)
    {
        Vector2[] points = refusal switch
        {
            "five corners" => [new(0f, 0f), new(8f, 0f), new(12f, 6f), new(4f, 10f), new(-4f, 6f)],
            "two corners" => [Vector2.Zero, new(8f, 0f)],
            "a reflex corner" => [new(0f, 0f), new(16f, 0f), new(4f, 4f), new(0f, 16f)],
            "two corners at one place" => [new(0f, 0f), new(16f, 0f), new(16f, 0.1f * Slop), new(0f, 16f)],
            _ => [Vector2.Zero, new(float.PositiveInfinity, 0f), new(0f, 8f)],
        };

        PolygonCollider2D beam = Beam();
        Shape2D before = beam.Shape;

        ArgumentException thrown = Assert.ThrowsAny<ArgumentException>(() => beam.SetPoints(points));

        Assert.Contains(message, thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.Bounds, beam.Shape.Bounds);
        Assert.Equal(3, beam.Shape.PointCount);
    }

    // A beam's own handler reshapes it on its first contact. An outline that still spans applies at once
    // and exits what it left on the next step. One that closes exits at once, as disabling does.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AContactHandler_ReshapesItsOwnPolygonAtOnce(bool closes)
    {
        Scene scene = new();
        PolygonCollider2D beam = Beam();
        List<string> log = Logged(beam);
        Aabb2D? placed = null;
        beam.ContactEntered += _ =>
        {
            if (log.Count == 1)
            {
                beam.SetPoints(closes ? [Vector2.Zero, new(20f, 0f), new(20f, 0f)] : [Vector2.Zero, new(20f, -8f), new(20f, 8f)]);
                placed = beam.Bounds;
            }
        };

        scene.Add(new Holder(Vector2.Zero, beam));
        scene.Add(new Holder(new Vector2(40f, -2f), new BoxCollider2D(new Vector2(4f, 4f))));
        using SimulationHost run = new(scene);
        run.Step();

        Assert.Equal(closes ? new[] { "+", "-" } : new[] { "+" }, log);
        Assert.Equal(closes ? new Aabb2D(new Vector2(0f, -8f), new Vector2(48f, 8f)) : new Aabb2D(new Vector2(0f, -8f), new Vector2(20f, 8f)), placed);

        run.Step();
        Assert.Equal(["+", "-"], log);
        Assert.True(beam.Enabled);
    }

    private static PolygonCollider2D Beam(float radius = 0f) =>
        new(Open, radius) { ReportsContacts = true, Detects = new(CollisionWorld2D.DefaultLayerName) };

    private static List<string> Logged(PolygonCollider2D beam)
    {
        List<string> log = [];
        beam.ContactEntered += _ => log.Add("+");
        beam.ContactExited += _ => log.Add("-");
        return log;
    }
}

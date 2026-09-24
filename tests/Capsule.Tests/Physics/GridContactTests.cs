using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

// Overlaps and sweeps against grid cells, where a one-way edge is a surface only from the side it
// faces and only along the extent it spans.
public sealed class GridContactTests
{
    [Fact]
    public void Overlap_ReportsEveryCellATouchingBoxCoversWithItsOwnCellAndLayers()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Span<Contact2D> contacts = stackalloc Contact2D[8];
        int count = world.OverlapBoxAll(CollisionFixtures.Box(20f, 12f, 16f, 8f), CollisionFilter.Everything, contacts);

        Assert.Equal(2, count);
        Assert.Equal((1, 1), (contacts[0].Target.CellX, contacts[0].Target.CellY));
        Assert.Equal((2, 1), (contacts[1].Target.CellX, contacts[1].Target.CellY));
        Assert.All(
            contacts[..count].ToArray(),
            contact => Assert.Equal(world.Layer(CollisionFixtures.Solid), contact.Target.Layer));
    }

    [Fact]
    public void Overlap_TreatsAOneWayCellAsItsEdgeRatherThanItsBody()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "----");

        Span<Contact2D> contacts = stackalloc Contact2D[8];

        // Wholly inside the cell but below its edge: the body is not the shape.
        Assert.Equal(0, world.OverlapBoxAll(CollisionFixtures.Box(20f, 20f, 8f, 8f), CollisionFilter.Everything, contacts));

        // Straddling the edge.
        Assert.Equal(1, world.OverlapBoxAll(CollisionFixtures.Box(20f, 12f, 8f, 8f), CollisionFilter.Everything, contacts));
    }

    // An edge is a surface only from the side it faces. Both boxes are within the skin of the plane;
    // only the one that has not passed through is touching anything.
    [Fact]
    public void OverlapCollider_ReportsAOneWayEdgeToAColliderAboveItAndNotToOneBelow()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "----");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        const float Half = 0.5f * CollisionTolerance.ContactSkin;

        // Resting on the face: inside the skin, on the outward side.
        ColliderHandle above = world.Add(box, new Vector2(20f, 8f - Half), world.Layer("body"));

        Span<Contact2D> contacts = stackalloc Contact2D[8];
        Assert.Equal(1, world.OverlapColliderAll(above, CollisionFilter.Everything, contacts));
        Assert.Equal(new Vector2(0f, -1f), contacts[0].Normal);
        world.Remove(above);

        // The same box the same distance the other side of the plane, having passed through it.
        ColliderHandle below = world.Add(box, new Vector2(20f, 16f + Half), world.Layer("body"));

        Assert.Equal(0, world.OverlapColliderAll(below, CollisionFilter.Everything, contacts));
    }

    // Sidedness is read off the authored line. Read off the narrowphase, an exact tie resolves towards
    // -X and -Y, and a box centred on the line would contact it or not by accident.
    [Fact]
    public void OverlapBox_CentredExactlyOnAOneWayEdge_TouchesItWithItsNormal()
    {
        CollisionWorld2D world = CollisionFixtures.OneWay();
        Span<Contact2D> contacts = stackalloc Contact2D[8];

        int count = world.OverlapBoxAll(
            Aabb2D.FromCenter(new Vector2(24f, 16f), new Vector2(8f, 8f)),
            CollisionFilter.Everything,
            contacts);

        Assert.Equal(1, count);
        Assert.Equal((1, 1), (contacts[0].Target.CellX, contacts[0].Target.CellY));
        Assert.Equal(new Vector2(0f, -1f), contacts[0].Normal);
    }

    // An edge is a segment with extent, so meeting it at one endpoint and nowhere along it is not
    // crossing it. The box starts flush against the line and off the near end of the edge by its own
    // width. The difference between passing through and landing is one slop of overlap along it.
    [Fact]
    public void ShapeCast_MeetingAnEdgeAtOneEndpointOnly_SweepsThroughIt()
    {
        CollisionWorld2D world = CollisionFixtures.OneWay();
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(4f, 4f));
        Vector2 origin = new(12f, 12f);
        Vector2 translation = new(0f, 32f);

        Assert.False(world.ShapeCast(box, origin, translation, CollisionFilter.Everything, out _));

        Assert.True(world.ShapeCast(
            box,
            origin + new Vector2(CollisionTolerance.LinearSlop, 0f),
            translation,
            CollisionFilter.Everything,
            out ShapeCastHit2D hit));

        Assert.Equal((1, 1), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
        Assert.Equal(0f, hit.Fraction);
    }

    // The sweep covers a band rather than a single line, so the cell showing the exposed face is visited
    // whatever the leading corner lands on.
    [Fact]
    public void ShapeCast_LandingItsLeadingCornerOnACellSeam_MeetsTheSurfaceRatherThanTheSeam()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Assert.True(world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)),
            Vector2.Zero,
            new Vector2(16f, 16f),
            CollisionFilter.Everything,
            out ShapeCastHit2D hit));

        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
        Assert.Equal(0.5f, hit.Fraction, 3);
    }

    // The normal is the face's own, not the narrowphase's: a rounded shape resting past the end of a
    // face is nearest its endpoint, where GJK answers with the diagonal from that corner.
    [Fact]
    public void OverlapCollider_PastTheEndOfAOneWayEdge_ReportsTheEdgesOwnNormal()
    {
        CollisionWorld2D world = new();

        // The only collidable cell is (1, 1), so the face spans x = 16..32 at y = 16.
        CollisionFixtures.Paint(world, "..", ".-");

        // Centred off the near end, close enough to the endpoint (16, 16) to be inside the skin.
        ColliderHandle circle = world.Add(
            Shape2D.Circle(Vector2.Zero, 4f),
            new Vector2(14f, 12.52f),
            world.Layer("body"));

        Span<Contact2D> contacts = stackalloc Contact2D[8];

        Assert.Equal(1, world.OverlapColliderAll(circle, CollisionFilter.Everything, contacts));
        Assert.Equal((1, 1), (contacts[0].Target.CellX, contacts[0].Target.CellY));
        Assert.Equal(new Vector2(0f, -1f), contacts[0].Normal);
    }
}

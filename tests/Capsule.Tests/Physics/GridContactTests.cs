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

    // An edge is a surface only from the side it faces. Both boxes are within the skin of the plane.
    // Only the one that has not passed through is touching anything.
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

    // An edge is a segment with extent. Meeting it at one endpoint and nowhere along it is not
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

    // A leading corner landing exactly on a seam meets the surface and not the seam. The sweep covers a
    // band rather than a single line. It visits the cell showing the exposed face whatever the corner
    // lands on, and a move slides along the surface for the rest of its way.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASweepLandingItsLeadingCornerOnACellSeam_MeetsTheSurfaceRatherThanTheSeam(bool move)
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "####");
        Vector2 translation = new(16f, 16f);

        (Vector2 normal, Vector2 landed) = move ? Move() : Cast();

        Assert.Equal(new Vector2(0f, -1f), normal);
        Assert.Equal(8f, landed.Y, CollisionFixtures.Tolerance);

        (Vector2 Normal, Vector2 Landed) Move()
        {
            Contact2D[] contacts = new Contact2D[4];
            MoveResult2D result = world.MoveBox(CollisionFixtures.Box(0f, 0f, 8f, 8f), translation, CollisionFilter.Everything, contacts);
            Assert.True(result.Blocked);
            Assert.Equal(16f, result.Translation.X, CollisionFixtures.Tolerance);

            return (contacts[0].Normal, result.Translation);
        }

        (Vector2 Normal, Vector2 Landed) Cast()
        {
            Assert.True(world.ShapeCast(
                Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)),
                Vector2.Zero,
                translation,
                CollisionFilter.Everything,
                out ShapeCastHit2D hit));

            return (hit.Normal, translation * hit.Fraction);
        }
    }

    // A filter turns the cells it excludes into empty space, faces included. A face a cell shares with an
    // excluded neighbour is that cell's outer face: the side of a wall beside an unseen wall, or the edge
    // of a one-way tile under an unseen solid tile. Every query starts 24 units out from the face.
    [Theory]
    [InlineData(false, "ray")]
    [InlineData(false, "cast")]
    [InlineData(false, "move")]
    [InlineData(false, "overlap")]
    [InlineData(true, "ray")]
    [InlineData(true, "cast")]
    [InlineData(true, "move")]
    [InlineData(true, "overlap")]
    public void AFaceCulledOnlyByANeighbourTheFilterExcludes_IsLiveToEveryQuery(bool oneWay, string query)
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, oneWay ? ["=", "-"] : ["=#.."]);
        CollisionFilter filter = world.CreateFilter(oneWay ? CollisionFixtures.Platform : CollisionFixtures.Climb);
        Vector2 face = oneWay ? new Vector2(8f, 16f) : new Vector2(16f, 8f);
        Vector2 normal = oneWay ? new Vector2(0f, -1f) : new Vector2(1f, 0f);
        Vector2 start = face + (24f * normal);
        Vector2 size = new(4f, 4f);
        Contact2D[] contacts = new Contact2D[4];

        (CollisionTarget target, Vector2 met) = query switch
        {
            "ray" => Ray(),
            "cast" => Cast(),
            "move" => Move(),
            _ => Overlap(),
        };

        Assert.True(target.IsGridCell);
        Assert.Equal(oneWay ? (0, 1) : (0, 0), (target.CellX, target.CellY));
        Assert.Equal(normal, met);

        (CollisionTarget, Vector2) Ray()
        {
            Assert.True(world.Raycast(start, -normal, 40f, filter, out RayHit2D hit));
            Assert.Equal(24f, hit.Distance, 3);

            return (hit.Target, hit.Normal);
        }

        (CollisionTarget, Vector2) Cast()
        {
            Assert.True(world.ShapeCast(Shape2D.Box(Vector2.Zero, size), start - (size / 2f), -40f * normal, filter, out ShapeCastHit2D hit));

            return (hit.Target, hit.Normal);
        }

        (CollisionTarget, Vector2) Move()
        {
            MoveResult2D result = world.MoveBox(Aabb2D.FromCenter(start, size), -40f * normal, filter, contacts);
            Assert.True(result.Blocked);
            Assert.Equal(-22f, Vector2.Dot(result.Translation, normal), CollisionFixtures.Tolerance);

            return (contacts[0].Target, contacts[0].Normal);
        }

        // Straddling the face by one unit, the box overlaps the cell behind it and the excluded one.
        (CollisionTarget, Vector2) Overlap()
        {
            Assert.Equal(1, world.OverlapBoxAll(Aabb2D.FromCenter(face + normal, size), filter, contacts));

            return (contacts[0].Target, contacts[0].Normal);
        }
    }

    // Every edge of the middle cell lies flush against a solid neighbour. A sweep out of it crosses no
    // face. The box starts inside it and reports it at fraction 0 whichever way it sweeps.
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(20f, 0f)]
    [InlineData(0f, -20f)]
    public void ShapeCast_StartingInsideASolidRun_ReportsTheCellItStartsInAtFractionZero(float x, float y)
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "###", "###", "###");

        Assert.True(world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)),
            new Vector2(20f, 20f),
            new Vector2(x, y),
            CollisionFilter.Everything,
            out ShapeCastHit2D hit));

        Assert.Equal((1, 1), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(0f, hit.Fraction);
        Assert.Equal(new Vector2(24f, 24f), hit.Point);
    }

    // A grid added after another grid was removed takes the freed slot. Overlaps, rays and sweeps
    // then still agree on which of two coinciding grids comes first.
    [Fact]
    public void GridsReaddedIntoFreedSlots_KeepOneOrderAcrossOverlapsRaysAndSweeps()
    {
        CollisionWorld2D world = new();
        CollisionGrid2D first = CollisionFixtures.Paint(world, "#");
        CollisionGrid2D second = CollisionFixtures.Paint(world, "#");
        world.Remove(first);
        CollisionGrid2D third = CollisionFixtures.Paint(world, "#");

        Span<Contact2D> contacts = stackalloc Contact2D[2];
        Span<RayHit2D> hits = stackalloc RayHit2D[2];
        Assert.Equal(2, world.OverlapBoxAll(CollisionFixtures.Box(4f, 4f, 8f, 8f), CollisionFilter.Everything, contacts));
        Assert.Equal(2, world.RaycastAll(new Vector2(8f, -8f), Vector2.UnitY, 32f, CollisionFilter.Everything, hits));
        Assert.True(world.ShapeCast(Shape2D.Box(new Vector2(4f, -12f), new Vector2(8f, 8f)), Vector2.Zero, new Vector2(0f, 16f), CollisionFilter.Everything, out ShapeCastHit2D swept));

        Assert.Equal(contacts[0].Target.Collider, hits[0].Target.Collider);
        Assert.Equal(contacts[0].Target.Collider, swept.Target.Collider);
        Assert.True(world.OverlapOrder(contacts[0].Target, contacts[1].Target) < 0);
    }
}

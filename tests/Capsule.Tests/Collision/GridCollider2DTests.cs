using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

public sealed class GridCollider2DTests
{
    [Fact]
    public void AddGrid_RejectsAGridThatDoesNotAddUp()
    {
        CollisionWorld2D world = new();

        Assert.Throws<ArgumentException>(() => world.AddGrid(16, 2, 2, [0, 0, 0], CollisionFixtures.Profiles(world)));
        Assert.Throws<ArgumentException>(() => world.AddGrid(16, 1, 1, [7], CollisionFixtures.Profiles(world)));
        Assert.Throws<ArgumentException>(() => world.AddGrid(16, 1, 1, [0], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddGrid(0, 1, 1, [0], CollisionFixtures.Profiles(world)));
    }

    // A cell on a layer with no face collides with nothing: a mistake rather than a way to spell
    // an empty cell, which is a profile on no layer.
    [Fact]
    public void AddGrid_RefusesAProfileOnLayersWithNoFaces()
    {
        CollisionWorld2D world = new();

        ArgumentException error = Assert.Throws<ArgumentException>(() => world.AddGrid(
            16,
            1,
            1,
            [0],
            [new CellProfile2D(world.Layer(CollisionFixtures.Solid), CellFaces2D.None)]));

        Assert.Contains("at least one side", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddGrid_SharesTheCallersCellArrayRatherThanCopyingIt()
    {
        CollisionWorld2D world = new();
        int[] cells = [0, 1];

        GridCollider2D map = world.AddGrid(16, 2, 1, cells, CollisionFixtures.Profiles(world));

        Assert.Null(map.LayerAt(0, 0));
        Assert.Equal(world.Layer(CollisionFixtures.Solid), map.LayerAt(1, 0));
        Assert.Equal(CellFaces2D.All, map.FacesAt(1, 0));
    }

    // A cell is on one layer, and a filter reaches it by naming that layer and no other.
    [Fact]
    public void ACellOnALayer_IsMatchedOnlyByAFilterNamingThatLayer()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "-");

        Assert.True(world.Raycast(
            new Vector2(8f, -8f),
            Vector2.UnitY,
            32f,
            world.Filter(CollisionFixtures.Platform),
            out RayHit2D platform));
        Assert.Equal(world.Layer(CollisionFixtures.Platform), platform.Target.Layer);

        Assert.False(world.Raycast(
            new Vector2(8f, -8f),
            Vector2.UnitY,
            32f,
            world.Filter(CollisionFixtures.Solid),
            out _));
    }

    // A tilemap handle names a grid, not one shape on one layer, so every per-collider accessor
    // refuses it rather than answering about the empty slot the grid occupies.
    [Fact]
    public void ThePerColliderAccessors_RefuseAGridHandleRatherThanDescribingItsSlot()
    {
        CollisionWorld2D world = new();
        GridCollider2D map = CollisionFixtures.Paint(world, "....", "####");
        ColliderHandle handle = map.Handle;

        Assert.Throws<ArgumentException>(() => world.SetFilter(handle, world.Layer("wall"), CollisionFilter.Everything));
        Assert.Throws<ArgumentException>(() => world.PositionOf(handle));
        Assert.Throws<ArgumentException>(() => world.ShapeOf(handle));
        Assert.Throws<ArgumentException>(() => world.LayerOf(handle));
        Assert.Throws<ArgumentException>(() => world.FilterOf(handle));
        Assert.Null(world.UserDataOf(handle));

        // The members that are about grids still take it, and the grid still answers as before.
        Assert.True(world.Contains(handle));
        Assert.Same(map, world.GridOf(handle));
        Assert.Equal(world.Layer(CollisionFixtures.Solid), map.LayerAt(0, 1));

        // And the refused SetFilter changed nothing a tile query reads.
        Assert.True(world.Raycast(new Vector2(8f, 0f), Vector2.UnitY, 64f, world.Filter(CollisionFixtures.Solid), out RayHit2D hit));
        Assert.Equal((0, 1), (hit.Target.CellX, hit.Target.CellY));
    }

    [Fact]
    public void ThePerCellAccessors_RejectACoordinateOffTheGrid()
    {
        CollisionWorld2D world = new();
        GridCollider2D map = CollisionFixtures.Paint(world, "##", "##");

        Assert.Throws<ArgumentOutOfRangeException>(() => map.LayerAt(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.LayerAt(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.FacesAt(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.FacesAt(0, -1));
    }

    [Fact]
    public void Raycast_StopsAtTheFirstSolidCellTheGridWalkReaches()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(
            world,
            ".....",
            ".....",
            "...#.");

        Assert.True(world.Raycast(new Vector2(56f, 8f), Vector2.UnitY, 200f, CollisionFilter.Everything, out RayHit2D downwards));
        Assert.True(downwards.Target.IsGridCell);
        Assert.Equal((3, 2), (downwards.Target.CellX, downwards.Target.CellY));
        Assert.Equal(24f, downwards.Distance, 3);
        Assert.Equal(new Vector2(0f, -1f), downwards.Normal);
    }

    [Fact]
    public void Raycast_MissesAnEmptyRowEntirely()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, ".....", "...#.");

        Assert.False(world.Raycast(new Vector2(8f, 8f), Vector2.UnitX, 200f, CollisionFilter.Everything, out _));
    }

    [Fact]
    public void Raycast_NeverReportsAFaceSharedWithAnotherSolidCell()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "####", "####");

        Assert.True(world.Raycast(new Vector2(-8f, 8f), Vector2.UnitX, 200f, CollisionFilter.Everything, out RayHit2D hit));

        Assert.Equal((0, 0), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(8f, hit.Distance, 3);
        Assert.Equal(new Vector2(-1f, 0f), hit.Normal);
    }

    [Fact]
    public void Raycast_CrossesATopFaceOnlyFromAbove()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "----");

        Assert.True(world.Raycast(new Vector2(24f, 0f), Vector2.UnitY, 64f, CollisionFilter.Everything, out RayHit2D above));
        Assert.Equal(16f, above.Distance, 3);
        Assert.Equal(new Vector2(0f, -1f), above.Normal);
        Assert.Equal(world.Layer(CollisionFixtures.Platform), above.Target.Layer);

        Assert.False(world.Raycast(new Vector2(24f, 30f), -Vector2.UnitY, 64f, CollisionFilter.Everything, out _));
        Assert.False(world.Raycast(new Vector2(0f, 20f), Vector2.UnitX, 64f, CollisionFilter.Everything, out _));
    }

    // The same primitive the other way up: a bottom face is what a body under reversed gravity
    // lands on.
    [Fact]
    public void Raycast_CrossesABottomFaceOnlyFromBelow()
    {
        CollisionWorld2D world = new();
        world.AddGrid(
            CollisionFixtures.TileSize,
            1,
            2,
            [0, 1],
            [new CellProfile2D(null), new CellProfile2D(world.Layer("ceiling"), CellFaces2D.Bottom)]);

        Assert.True(world.Raycast(new Vector2(8f, 48f), -Vector2.UnitY, 64f, CollisionFilter.Everything, out RayHit2D below));
        Assert.Equal(16f, below.Distance, 3);
        Assert.Equal(new Vector2(0f, 1f), below.Normal);

        Assert.False(world.Raycast(new Vector2(8f, 0f), Vector2.UnitY, 64f, CollisionFilter.Everything, out _));
    }

    // A filter turns the cells it excludes into empty space, faces included: the seam a wall shares
    // with a wall the query cannot see is that wall's own outer face, not an interior one.
    [Fact]
    public void Raycast_ReachesTheFaceASolidCellSharesWithOneTheFilterExcludes()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "=#..");

        Assert.True(world.Raycast(new Vector2(40f, 8f), -Vector2.UnitX, 64f, CollisionFilter.Everything, out RayHit2D whole));
        Assert.Equal((1, 0), (whole.Target.CellX, whole.Target.CellY));
        Assert.Equal(8f, whole.Distance, 3);

        Assert.True(world.Raycast(
            new Vector2(40f, 8f),
            -Vector2.UnitX,
            64f,
            world.Filter(CollisionFixtures.Climb),
            out RayHit2D climbing));

        Assert.Equal((0, 0), (climbing.Target.CellX, climbing.Target.CellY));
        Assert.Equal(24f, climbing.Distance, 3);
        Assert.Equal(new Vector2(1f, 0f), climbing.Normal);
    }

    // A diagonal reaching a cell corner dead on touches both cells it separates. The walk must not
    // commit to one and report nothing because the face it picked is the seam between the two.
    [Theory]
    [InlineData(8f, 8f, 1f, 1f)]
    [InlineData(56f, 8f, -1f, 1f)]
    public void Raycast_FindsTheExposedFaceWhereADiagonalCrossesExactlyThroughACellCorner(
        float originX,
        float originY,
        float directionX,
        float directionY)
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Assert.True(world.Raycast(
            new Vector2(originX, originY),
            new Vector2(directionX, directionY),
            200f,
            CollisionFilter.Everything,
            out RayHit2D hit));

        Assert.Equal(new Vector2(0f, -1f), hit.Normal);
        Assert.Equal(16f, hit.Point.Y, 3);
        Assert.Equal(1, hit.Target.CellY);
    }

    // Upwards through the same corner: the underside of the run is one flat face, and the seam
    // between two of its cells is not a surface however exactly the ray meets it.
    [Fact]
    public void Raycast_CrossingACornerFromBelowMeetsTheUndersideRatherThanTheSeam()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "####", "....");

        Assert.True(world.Raycast(new Vector2(8f, 24f), new Vector2(1f, -1f), 200f, CollisionFilter.Everything, out RayHit2D hit));

        Assert.Equal(new Vector2(0f, 1f), hit.Normal);
        Assert.Equal(16f, hit.Point.Y, 3);
        Assert.Equal(0, hit.Target.CellY);
    }

    // The corner probe must not invent a neighbour: with one cell solid, the answer is its own
    // exposed side.
    [Fact]
    public void Raycast_AtACornerWithOnlyOneSolidCell_ReportsThatCellsOwnFace()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", ".###");

        Assert.True(world.Raycast(new Vector2(8f, 8f), new Vector2(1f, 1f), 200f, CollisionFilter.Everything, out RayHit2D hit));

        Assert.Equal((1, 1), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(new Vector2(16f, 16f), hit.Point);
    }

    // A corner buried inside the terrain exposes nothing: the ray meets the outside of the block.
    [Fact]
    public void Raycast_ReportsNothingAtACornerWhoseFacesAreAllInterior()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", ".###", ".###");

        Assert.True(world.Raycast(new Vector2(8f, 8f), new Vector2(1f, 1f), 200f, CollisionFilter.Everything, out RayHit2D hit));

        // The block's own outer corner at (16,16), not the interior seam at (32,32).
        Assert.Equal(new Vector2(16f, 16f), hit.Point);
        Assert.Equal((1, 1), (hit.Target.CellX, hit.Target.CellY));
    }

    // The sweep covers a band rather than a single line, so the cell showing the exposed face is
    // visited whatever the leading corner lands on.
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

    [Fact]
    public void Raycast_RespectsAFilterThatExcludesATileType()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "----", "####");
        CollisionFilter solidOnly = world.Filter(CollisionFixtures.Solid);

        Assert.True(world.Raycast(new Vector2(24f, 0f), Vector2.UnitY, 96f, solidOnly, out RayHit2D hit));

        Assert.Equal(32f, hit.Distance, 3);
        Assert.Equal(CollisionFixtures.Solid, world.NameOf(hit.Target.Layer));
    }

    // A tilemap is a collider like any other as far as the ignore argument is concerned.
    [Fact]
    public void EveryVerb_PassesThroughATilemapGivenAsTheIgnoredCollider()
    {
        CollisionWorld2D world = new();
        GridCollider2D floor = CollisionFixtures.Paint(world, "....", "####");
        GridCollider2D ceiling = CollisionFixtures.Paint(world, "####", "....");
        ColliderHandle body = world.Add(
            Shape2D.Box(new Vector2(20f, 20f), new Vector2(8f, 8f)),
            Vector2.Zero,
            world.Layer(CollisionFixtures.Solid),
            CollisionFilter.None);

        // Downwards from inside the empty row: the floor is the only thing below, and it is ignored.
        Assert.False(world.Raycast(new Vector2(56f, 20f), Vector2.UnitY, 64f, CollisionFilter.Everything, out _, floor.Handle));

        // The ceiling above it is not, so the same ray upwards still lands.
        Assert.True(world.Raycast(new Vector2(56f, 20f), -Vector2.UnitY, 64f, CollisionFilter.Everything, out RayHit2D up, floor.Handle));
        Assert.Equal(ceiling.Handle, up.Target.Collider);

        Span<RayHit2D> hits = stackalloc RayHit2D[8];
        int count = world.RaycastAll(new Vector2(56f, 20f), Vector2.UnitY, 64f, CollisionFilter.Everything, hits, floor.Handle);
        Assert.Equal(0, count);

        // A box overlapping the floor row reports the body beside it and nothing of the floor.
        Span<Contact2D> contacts = stackalloc Contact2D[8];
        Assert.Equal(
            1,
            world.OverlapBox(CollisionFixtures.Box(20f, 18f, 12f, 12f), CollisionFilter.Everything, contacts, floor.Handle));
        Assert.Equal(body, contacts[0].Target.Collider);

        // And a move down through the floor is not stopped by it.
        MoveResult2D through = world.MoveBox(
            CollisionFixtures.Box(52f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default,
            floor.Handle);
        Assert.False(through.BlockedY);
        Assert.Equal(20f, through.Translation.Y, 2f * CollisionWorld2D.LinearSlop);

        // The same move with nothing ignored still lands on it.
        Assert.True(world.MoveBox(
            CollisionFixtures.Box(52f, 4f, 8f, 8f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            default).BlockedY);

        Assert.False(world.ShapeCast(
            Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f)),
            new Vector2(52f, 4f),
            new Vector2(0f, 20f),
            CollisionFilter.Everything,
            out _,
            floor.Handle));
    }

    [Fact]
    public void Overlap_ReportsEveryCellATouchingBoxCoversWithItsOwnCellAndLayers()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "####");

        Span<Contact2D> contacts = stackalloc Contact2D[8];
        int count = world.OverlapBox(CollisionFixtures.Box(20f, 12f, 16f, 8f), CollisionFilter.Everything, contacts);

        Assert.Equal(2, count);
        Assert.Equal((1, 1), (contacts[0].Target.CellX, contacts[0].Target.CellY));
        Assert.Equal((2, 1), (contacts[1].Target.CellX, contacts[1].Target.CellY));
        Assert.All(
            contacts[..count].ToArray(),
            contact => Assert.Equal(world.Layer(CollisionFixtures.Solid), contact.Target.Layer));
    }

    [Fact]
    public void Overlap_TreatsATopFaceCellAsItsEdgeRatherThanItsBody()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "----");

        Span<Contact2D> contacts = stackalloc Contact2D[8];

        // Wholly inside the cell but below its edge: the body is not the shape.
        Assert.Equal(0, world.OverlapBox(CollisionFixtures.Box(20f, 20f, 8f, 8f), CollisionFilter.Everything, contacts));

        // Straddling the edge.
        Assert.Equal(1, world.OverlapBox(CollisionFixtures.Box(20f, 12f, 8f, 8f), CollisionFilter.Everything, contacts));
    }

    // A face is a surface only from the side it faces. Both boxes are within the skin of the
    // plane; only the one that has not passed through is touching anything.
    [Fact]
    public void OverlapCollider_ReportsATopFaceToAColliderAboveItAndNotToOneBelow()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "----");
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        const float Half = 0.5f * CollisionWorld2D.ContactSkin;

        // Resting on the face: inside the skin, on the outward side.
        ColliderHandle above = world.Add(box, new Vector2(20f, 8f - Half), world.Layer("body"), CollisionFilter.Everything);

        Span<Contact2D> contacts = stackalloc Contact2D[8];
        Assert.Equal(1, world.OverlapCollider(above, contacts));
        Assert.Equal(new Vector2(0f, -1f), contacts[0].Normal);
        world.Remove(above);

        // The same box the same distance the other side of the plane, having passed through it.
        ColliderHandle below = world.Add(box, new Vector2(20f, 16f + Half), world.Layer("body"), CollisionFilter.Everything);

        Assert.Equal(0, world.OverlapCollider(below, contacts));
    }

    // Sidedness is read off the authored plane, so all four faces answer alike. Read off the
    // narrowphase, an exact tie resolves towards -X and -Y and a box centred on the plane would
    // contact Top and Left and nothing for Bottom and Right.
    [Theory]
    [InlineData(CellFaces2D.Top, 24f, 16f, 0f, -1f)]
    [InlineData(CellFaces2D.Bottom, 24f, 32f, 0f, 1f)]
    [InlineData(CellFaces2D.Left, 16f, 24f, -1f, 0f)]
    [InlineData(CellFaces2D.Right, 32f, 24f, 1f, 0f)]
    public void OverlapBox_CentredExactlyOnAFacePlane_TouchesItWithThatFacesNormal(
        CellFaces2D face,
        float centerX,
        float centerY,
        float outwardX,
        float outwardY)
    {
        CollisionWorld2D world = OneFace(face);
        Span<Contact2D> contacts = stackalloc Contact2D[8];

        int count = world.OverlapBox(
            Aabb2D.FromCenter(new Vector2(centerX, centerY), new Vector2(8f, 8f)),
            CollisionFilter.Everything,
            contacts);

        Assert.Equal(1, count);
        Assert.Equal((1, 1), (contacts[0].Target.CellX, contacts[0].Target.CellY));
        Assert.Equal(new Vector2(outwardX, outwardY), contacts[0].Normal);
    }

    // The mirror of the boundary: a centre a skin past the plane has gone through, and the face it
    // came through is behind it. The box still straddles the plane, so only sidedness rejects it.
    [Theory]
    [InlineData(CellFaces2D.Top, 24f, 16f, 0f, 1f)]
    [InlineData(CellFaces2D.Bottom, 24f, 32f, 0f, -1f)]
    [InlineData(CellFaces2D.Left, 16f, 24f, 1f, 0f)]
    [InlineData(CellFaces2D.Right, 32f, 24f, -1f, 0f)]
    public void OverlapBox_CentredPastAFacePlane_TouchesNothing(
        CellFaces2D face,
        float planeX,
        float planeY,
        float inwardX,
        float inwardY)
    {
        CollisionWorld2D world = OneFace(face);
        Vector2 center = new Vector2(planeX, planeY)
            + (new Vector2(inwardX, inwardY) * CollisionWorld2D.ContactSkin);

        Span<Contact2D> contacts = stackalloc Contact2D[8];

        Assert.Equal(
            0,
            world.OverlapBox(
                Aabb2D.FromCenter(center, new Vector2(8f, 8f)),
                CollisionFilter.Everything,
                contacts));
    }

    // A face is a segment with extent, so meeting it at one endpoint and nowhere along it is not
    // crossing it. Each box starts flush against the plane and off the near end of the edge by its
    // own width; the difference between passing through and landing is one slop of overlap along it.
    [Theory]
    [InlineData(CellFaces2D.Top, 12f, 12f, 0f, 32f, 0f, -1f)]
    [InlineData(CellFaces2D.Bottom, 12f, 32f, 0f, -32f, 0f, 1f)]
    [InlineData(CellFaces2D.Left, 12f, 12f, 32f, 0f, -1f, 0f)]
    [InlineData(CellFaces2D.Right, 32f, 12f, -32f, 0f, 1f, 0f)]
    public void ShapeCast_MeetingAFaceAtOneEndpointOnly_SweepsThroughIt(
        CellFaces2D face,
        float originX,
        float originY,
        float translationX,
        float translationY,
        float outwardX,
        float outwardY)
    {
        CollisionWorld2D world = OneFace(face);
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(4f, 4f));
        Vector2 origin = new(originX, originY);
        Vector2 translation = new(translationX, translationY);
        Vector2 outward = new(outwardX, outwardY);

        Assert.False(world.ShapeCast(box, origin, translation, CollisionFilter.Everything, out _));

        Vector2 along = new Vector2(MathF.Abs(outwardY), MathF.Abs(outwardX)) * CollisionWorld2D.LinearSlop;

        Assert.True(world.ShapeCast(
            box,
            origin + along,
            translation,
            CollisionFilter.Everything,
            out ShapeCastHit2D hit));

        Assert.Equal((1, 1), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(outward, hit.Normal);
        Assert.Equal(0f, hit.Fraction);
    }

    // A 3x3 grid whose only collidable cell is the middle one, carrying exactly one face. That cell
    // spans x = 16..32 and y = 16..32, so each face's plane is one of those four coordinates.
    private static CollisionWorld2D OneFace(CellFaces2D face)
    {
        CollisionWorld2D world = new();
        world.AddGrid(
            CollisionFixtures.TileSize,
            3,
            3,
            [0, 0, 0, 0, 1, 0, 0, 0, 0],
            [new CellProfile2D(null), new CellProfile2D(world.Layer("ledge"), face)]);

        return world;
    }

    // The normal is the face's own, not the narrowphase's: a rounded shape resting past the end of
    // a face is nearest its endpoint, where GJK answers with the diagonal from that corner.
    [Fact]
    public void OverlapCollider_PastTheEndOfATopFace_ReportsTheFacesOwnNormal()
    {
        CollisionWorld2D world = new();

        // The only collidable cell is (1, 1), so the face spans x = 16..32 at y = 16.
        CollisionFixtures.Paint(world, "..", ".-");

        // Centred off the near end, close enough to the endpoint (16, 16) to be inside the skin.
        ColliderHandle circle = world.Add(
            Shape2D.Circle(Vector2.Zero, 4f),
            new Vector2(14f, 12.52f),
            world.Layer("body"),
            CollisionFilter.Everything);

        Span<Contact2D> contacts = stackalloc Contact2D[8];

        Assert.Equal(1, world.OverlapCollider(circle, contacts));
        Assert.Equal((1, 1), (contacts[0].Target.CellX, contacts[0].Target.CellY));
        Assert.Equal(new Vector2(0f, -1f), contacts[0].Normal);
    }
}

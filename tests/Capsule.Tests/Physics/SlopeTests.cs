using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Physics;

// Slope tiles, the slide a move makes along them, and the grounded body that walks them.
public sealed class SlopeTests
{
    private const float Speed = 2f;

    // A floating move into a slope keeps what the slope does not take. The box's bottom-right corner
    // meets the diagonal x + y = 64 after 4 units, and the other 16 slide up it as (8, -8).
    [Fact]
    public void MoveBox_IntoADiagonalWall_SlidesAlongItInsteadOfStopping()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "....", "../#", "####");

        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(20f, 24f - CollisionTolerance.LinearSlop, 8f, 8f),
            new Vector2(20f, 0f),
            CollisionFilter.Everything,
            default);

        Assert.True(result.Blocked);
        Assert.Equal(12f, result.Translation.X, 0.05f);
        Assert.Equal(-8f, result.Translation.Y, 0.05f);
    }

    // A box rising past the top vertex of a 1:2 slope grazes it with its bottom-right corner, which
    // starts half a unit below the vertex. The face chosen at the graze is the box's floor, not the
    // slope's side it started level with, and the box keeps its travel across.
    [Fact]
    public void MoveBox_GrazingASlopesTopVertex_KeepsItsTravelAcross()
    {
        CollisionWorld2D world = new();
        world.Add(
            Shape2D.Polygon([new(0f, 0f), new(32f, 16f), new(0f, 16f)]),
            Vector2.Zero,
            world.Layer(CollisionFixtures.Solid));

        MoveResult2D result = world.MoveBox(
            CollisionFixtures.Box(-28f, -7.5055f, 8f, 8f),
            new Vector2(40f, -1f),
            CollisionFilter.Everything,
            default);

        Assert.Equal(40f, result.Translation.X, CollisionFixtures.Tolerance);
    }

    // The slope's right side and the box's left side cover each other fully, so neither is a surface,
    // and the ground under the slope culls its bottom.
    // A filter that cannot see the box turns it back into empty space, and the slope's side is live
    // again.
    [Fact]
    public void ASlopeBesideASolidBox_SharesNoLiveEdgeWithIt()
    {
        CollisionWorld2D world = new();
        GridCollider2D grid = CollisionFixtures.Paint(world, "/=", "##");

        Assert.Equal(CellState2D.Edges | GridCollider2D.EdgeBit(0), grid.StateAt(0, 0));
        Assert.Equal(CellState2D.None, grid.StateAt(1, 0) & CellState2D.FaceMinX);

        Assert.True(world.Raycast(
            new Vector2(24f, 12f),
            -Vector2.UnitX,
            16f,
            world.CreateFilter(CollisionFixtures.Solid),
            out RayHit2D hit));
        Assert.Equal((0, 0), (hit.Target.CellX, hit.Target.CellY));
        Assert.Equal(new Vector2(1f, 0f), hit.Normal);
        Assert.Equal(8f, hit.Distance, 3);
    }

    // Up a two-tile slope, across the top and down the far side. Every step ends on a floor and covers
    // its whole speed across, including the steps that turn onto a new surface or follow a crest down.
    [Fact]
    public void AGroundedBody_WalksOverAHill_OnTheFloorEveryStep_AtOneSpeedAcross()
    {
        Scene scene = SceneFixtures.Terrain(
            "..............",
            "..............",
            "..../#\\.......",
            ".../###\\......",
            "##############");
        SceneFixtures.Body body = Grounded(scene, new Vector2(8f, 40f));

        for (int step = 0; step < 70; step++)
        {
            MoveResult2D result = body.Mover.Move(new Vector2(Speed, 0.5f));

            Assert.True(body.Mover.IsOnFloor, $"step {step} left the floor at {body.Position}");
            Assert.Equal(Speed, result.Translation.X, 0.02f);
        }

        Assert.True(body.Position.X > 128f);
        Assert.Equal(56f, body.Position.Y, CollisionFixtures.Tolerance);
    }

    // A walk on a slope covers its speed horizontally by default. With KeepsHorizontalSpeedOnSlopes off
    // it covers its speed along the surface. The wedge rises to the right one unit for every `run`.
    [Theory]
    [InlineData(2f, 1f, true)]
    [InlineData(2f, -1f, true)]
    [InlineData(4f, 1f, true)]
    [InlineData(4f, -1f, true)]
    [InlineData(2f, 1f, false)]
    [InlineData(4f, -1f, false)]
    public void AGroundedBodyWalkingASlope_CoversItsSpeedAcross_OrAlongTheSurfaceWhenOff(float run, float way, bool keepsHorizontalSpeed)
    {
        Scene scene = new(SceneFixtures.Content(SceneFixtures.RoomWithoutTerrain(), SceneFixtures.Registry()));
        scene.Add(new Wedge(run));
        SceneFixtures.Body body = Grounded(scene, new Vector2(124f, 20f));
        body.Mover.KeepsHorizontalSpeedOnSlopes = keepsHorizontalSpeed;
        Vector2 start = body.Position;

        for (int step = 0; step < 20; step++)
        {
            MoveResult2D result = body.Mover.Move(new Vector2(way * Speed, 0.5f));

            Assert.True(body.Mover.IsOnFloor, $"step {step} left the floor at {body.Position}");
            float covered = keepsHorizontalSpeed ? MathF.Abs(result.Translation.X) : result.Translation.Length();
            Assert.Equal(Speed, covered, 0.01f);
        }

        Assert.Equal(-1f / run, (body.Position.Y - start.Y) / (body.Position.X - start.X), 0.01f);
    }

    // A floor stops a fall outright, so gravity alone never walks a body down a slope.
    [Fact]
    public void AGroundedBodyStandingOnASlope_DoesNotDrift()
    {
        Scene scene = SceneFixtures.Terrain("....", "./..", "####");
        SceneFixtures.Body body = Grounded(scene, new Vector2(20f, 0f));
        Vector2 landed = body.Position;
        Assert.True(body.Mover.FloorNormal.X < 0f);

        for (int step = 0; step < 120; step++)
        {
            body.Mover.Move(new Vector2(0f, 0.5f));
            Assert.True(body.Mover.IsOnFloor);
        }

        Assert.Equal(landed, body.Position);
    }

    // Down a 1:2 slope whose foot meets a 1:4 slope starting 4 units higher. The box's bottom lands on
    // the step's corner and walks on from there. At no step does it end below the terrain anywhere
    // under it, which a box sinking into the corner and falling through the ground would. The mirrored
    // run walks left down the same terrain flipped.
    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void AGroundedBox_WalkingDownOntoAStepsCorner_StaysOnTopOfIt(float way)
    {
        Scene scene = new(SceneFixtures.Content(
            new SceneDocument([new TileMapPlacement(SceneFixtures.TerrainId, StepGrid(way < 0f))], SceneFixtures.TerrainId + 1),
            SceneFixtures.Registry()));
        SceneFixtures.Body body = new(new Vector2(way > 0f ? 8f : 120f, -1f), blocksOn: "solid");
        body.Collider.Size = new Vector2(16f, 32f);
        body.Collider.Offset = new Vector2(-8f, -32f);
        body.Mover.Mode = BodyMode.Grounded;
        scene.Add(body);
        CollisionFilter solid = scene.Collision.CreateFilter("solid");
        float fall = 0f;

        for (int step = 0; step < 60; step++)
        {
            fall += 900f / 60f;
            body.Mover.Move(new Vector2(way * 105f / 60f, fall / 60f));
            if (body.Mover.IsOnFloor)
            {
                fall = 0f;
            }

            for (float x = MathF.Ceiling(body.Position.X - 8f) + 0.5f; x < body.Position.X + 8f; x++)
            {
                Assert.True(scene.Collision.Raycast(new Vector2(x, -64f), Vector2.UnitY, 128f, solid, out RayHit2D ground));
                Assert.True(
                    body.Position.Y <= ground.Distance - 64f + CollisionFixtures.Tolerance,
                    $"step {step} sank to {body.Position}, below the ground at x = {x}");
            }
        }

        Assert.True(body.Mover.IsOnFloor);
        Assert.True(way * (body.Position.X - 64f) > 32f);
    }

    // One long walk from flat ground meets the foot of a 1:2 slope with the box's bottom corner level
    // with the foot's vertex, and carries on up the slope. It ends on the slope's face, never in it,
    // and reports that face's normal. The mirrored run walks left up the same slope flipped.
    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void AGroundedBox_WalkingOverASlopesFoot_ClimbsItsFace(float way)
    {
        Scene scene = new();
        scene.Add(new Hull(Mirrored(way, new(0f, 64f), new(160f, 64f), new(160f, 80f), new(0f, 80f))));
        scene.Add(new Hull(Mirrored(way, new(48f, 64f), new(80f, 48f), new(80f, 64f))));
        SceneFixtures.Body body = Grounded(scene, new Vector2(way > 0f ? 16f : 136f, 55f));

        body.Mover.Move(new Vector2(way * 40f, 0.5f));

        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(way > 0f ? 56f : 96f, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(48f, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.Equal(-way * 0.4472136f, body.Mover.FloorNormal.X, 1e-4f);
        Assert.Equal(-0.8944272f, body.Mover.FloorNormal.Y, 1e-4f);
    }

    // A box falls with its bottom corner onto the top vertex of a 1:2 slope and lands on it, on either
    // side of the vertex. A vertical fall is a slop narrower than the box, so its corner meets the
    // slope's face just past the vertex and reports that face. It stops a slop clear of the face, which
    // rests the box up to the contact skin above the vertex and never below it.
    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void AGroundedBox_FallingOntoASlopesTopVertex_LandsOnIt(float way)
    {
        Scene scene = new();
        scene.Add(new Hull(Mirrored(way, new(64f, 48f), new(96f, 64f), new(64f, 64f))));
        SceneFixtures.Body body = new(new Vector2(way > 0f ? 64f : 88f, 32f), blocksOn: "solid");
        body.Mover.Mode = BodyMode.Grounded;
        scene.Add(body);

        body.Mover.Move(new Vector2(0f, 40f));

        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(way > 0f ? 64f : 88f, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(way * 0.4472136f, body.Mover.FloorNormal.X, 1e-4f);
        Assert.Equal(-0.8944272f, body.Mover.FloorNormal.Y, 1e-4f);
        Assert.InRange(body.Position.Y, 40f - CollisionTolerance.ContactSkin, 40f);
    }

    // Points as given for `way` 1, or reflected about x = 80 for -1.
    private static Vector2[] Mirrored(float way, params Vector2[] points)
    {
        for (int index = 0; index < points.Length; index++)
        {
            points[index].X = way > 0f ? points[index].X : 160f - points[index].X;
        }

        return points;
    }

    // Lands a grounded 8x8 body from where it starts.
    private static SceneFixtures.Body Grounded(Scene scene, Vector2 position)
    {
        SceneFixtures.Body body = new(position, blocksOn: "solid");
        body.Mover.Mode = BodyMode.Grounded;
        scene.Add(body);
        body.Mover.Move(new Vector2(0f, 40f));
        Assert.True(body.Mover.IsOnFloor);

        return body;
    }

    // A 256-wide wedge on "solid" that rises to the right one unit for every `run`.
    private sealed class Wedge : Entity
    {
        internal Wedge(float run)
            : base(Vector2.Zero) =>
            Add(new PolygonCollider2D([new(0f, 256f / run), new(256f, 0f), new(256f, 256f / run)]) { Layer = "solid" });
    }

    // A convex polygon collider on "solid".
    private sealed class Hull : Entity
    {
        internal Hull(Vector2[] points)
            : base(Vector2.Zero) =>
            Add(new PolygonCollider2D(points) { Layer = "solid" });
    }

    // Flat ground, a 1:4 descent, a 1:2 descent whose foot is at 16, and a 1:4 descent that starts
    // again at 12, with ground below all of it. Mirrored, the same terrain descends to the left.
    private static TileGrid StepGrid(bool mirrored)
    {
        TileType Piece(string name, params Vector2[] points)
        {
            if (mirrored)
            {
                for (int index = 0; index < points.Length; index++)
                {
                    points[index].X = SceneFixtures.TileSize - points[index].X;
                }
            }

            return new() { Name = name, Cell = 0, Layer = "solid", Shape = Shape2D.Polygon(points) };
        }

        int[] cells =
        [
            1, 1, 2, 3, 4, 5, 0, 0,
            1, 1, 1, 1, 1, 1, 6, 4,
            1, 1, 1, 1, 1, 1, 1, 1,
        ];
        if (mirrored)
        {
            for (int row = 0; row < 3; row++)
            {
                Array.Reverse(cells, row * 8, 8);
            }
        }

        return new TileGrid(
            SceneFixtures.TileSize,
            8,
            3,
            [
                TileGrid.EmptyTile,
                new TileType { Name = "solid", Cell = 0, Layer = "solid" },
                Piece("quarter-top", new(0f, 0f), new(16f, 4f), new(16f, 16f), new(0f, 16f)),
                Piece("quarter-upper", new(0f, 4f), new(16f, 8f), new(16f, 16f), new(0f, 16f)),
                Piece("half-lower", new(0f, 8f), new(16f, 16f), new(0f, 16f)),
                Piece("quarter-bottom", new(0f, 12f), new(16f, 16f), new(0f, 16f)),
                Piece("half-upper", new(0f, 0f), new(16f, 8f), new(16f, 16f), new(0f, 16f)),
            ],
            cells,
            SceneFixtures.Atlas,
            1);
    }
}

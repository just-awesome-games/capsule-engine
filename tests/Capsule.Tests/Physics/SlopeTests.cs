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

    // Up a two-tile slope, across the top and down the far side. Every step ends on a floor, and a step
    // that stays on one surface covers its whole length along it.
    [Fact]
    public void AGroundedBody_WalksOverAHill_OnTheFloorEveryStep_AtOneSpeedAlongTheSurface()
    {
        Scene scene = SceneFixtures.Terrain(
            "..............",
            "..............",
            "..../#\\.......",
            ".../###\\......",
            "##############");
        SceneFixtures.Body body = Grounded(scene, new Vector2(8f, 40f));

        for (int step = 0; step < 90; step++)
        {
            Vector2 floor = body.Mover.FloorNormal;
            MoveResult2D result = body.Mover.Move(new Vector2(Speed, 0.5f));

            Assert.True(body.Mover.IsOnFloor, $"step {step} left the floor at {body.Position}");
            if (body.Mover.FloorNormal == floor)
            {
                Assert.Equal(Speed, result.Translation.Length(), 0.02f);
            }
        }

        Assert.True(body.Position.X > 128f);
        Assert.Equal(56f, body.Position.Y, CollisionFixtures.Tolerance);
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

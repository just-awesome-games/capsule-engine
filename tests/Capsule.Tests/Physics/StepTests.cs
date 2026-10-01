using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Physics;

// A grounded body's StepHeight: stepping up onto a lip and keeping to a floor below a drop.
public sealed class StepTests
{
    private const float Speed = 2f;

    private const float Gravity = 0.5f;

    private const float GroundTop = 64f;

    private const float Edge = 8f;

    [Fact]
    public void StepHeight_RejectsANonFiniteHeight()
    {
        KinematicBody2D body = new(new BoxCollider2D(new Vector2(Edge, Edge)));

        Assert.Throws<ArgumentOutOfRangeException>(() => body.StepHeight = float.NaN);
    }

    // The lip spans x = 48 to 64 on ground whose top is y = 64, as a tile or as a box collider. A body
    // walking right climbs it within one step when it is no taller than StepHeight and there is room
    // above to rise. It never leaves the floor and covers its whole speed across on every step.
    // Otherwise the lip stops it as a wall, exactly as with StepHeight 0.
    [Theory]
    [InlineData(4f, 4f, 0f, true, true)]
    [InlineData(4f, 4f, 0f, false, true)]
    [InlineData(1f, 6f, 0f, false, true)]
    [InlineData(4f, 6f, 6f, true, true)]
    [InlineData(5f, 4f, 0f, true, false)]
    [InlineData(5f, 4f, 0f, false, false)]
    [InlineData(4f, 6f, 2f, false, false)]
    [InlineData(4f, 0f, 0f, false, false)]
    public void AGroundedBodyWalkingIntoALip_ClimbsItWithinStepHeightAndHeadroom_OrIsStoppedByIt(
        float lip, float stepHeight, float headroom, bool tiles, bool climbs)
    {
        Scene scene = tiles ? LipGrid(lip) : new Scene();
        if (!tiles)
        {
            scene.Add(new Block(new Vector2(0f, GroundTop), new Vector2(128f, 16f)));
            scene.Add(new Block(new Vector2(48f, GroundTop - lip), new Vector2(16f, lip)));
        }

        if (headroom > 0f)
        {
            scene.Add(new Block(new Vector2(0f, 0f), new Vector2(128f, GroundTop - Edge - headroom)));
        }

        SceneFixtures.Body body = Grounded(scene, 36f, stepHeight);

        for (int step = 0; step < 10; step++)
        {
            MoveResult2D result = body.Mover.Move(new Vector2(Speed, Gravity));

            Assert.True(body.Mover.IsOnFloor, $"step {step} left the floor at {body.Position}");
            if (climbs)
            {
                Assert.False(body.Mover.IsOnWall);
                Assert.Equal(Speed, result.Translation.X, CollisionFixtures.Tolerance);
            }
        }

        Assert.Equal(climbs ? 56f : 40f, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(GroundTop - Edge - (climbs ? lip : 0f), body.Position.Y, CollisionFixtures.Tolerance);
        Assert.Equal(-Vector2.UnitY, body.Mover.FloorNormal);
        Assert.Equal(!climbs, body.Mover.IsOnWall);
    }

    // A body that did not end its last move on a floor, or whose move rises, meets a lip as a wall.
    [Theory]
    [InlineData(false, 54f, Gravity)]
    [InlineData(true, GroundTop - Edge, -1f)]
    public void ABodyAirborneOrRising_NeverStepsUp(bool grounded, float y, float rise)
    {
        Scene scene = new();
        scene.Add(new Block(new Vector2(0f, GroundTop), new Vector2(128f, 16f)));
        scene.Add(new Block(new Vector2(48f, GroundTop - 4f), new Vector2(16f, 4f)));
        SceneFixtures.Body body = new(new Vector2(40f - CollisionTolerance.LinearSlop, y), blocksOn: "solid");
        body.Mover.Mode = BodyMode.Grounded;
        body.Mover.StepHeight = 8f;
        scene.Add(body);
        if (grounded)
        {
            body.Mover.Move(new Vector2(0f, Gravity));
            Assert.True(body.Mover.IsOnFloor);
        }

        body.Mover.Move(new Vector2(Speed, rise));

        Assert.True(body.Mover.IsOnWall);
        Assert.Equal(40f, body.Position.X, CollisionFixtures.Tolerance);
    }

    // The ground steps down at x = 64 by `drop`, onto solid ground or a one-way ledge. A body walking off
    // the edge stays on the floor at every step when the drop is within StepHeight, and lands on the
    // lower floor. A drop even slightly deeper, or StepHeight 0, leaves it airborne for a step.
    [Theory]
    [InlineData(6f, 6f, false, true)]
    [InlineData(6f, 6f, true, true)]
    [InlineData(6f, 0f, false, false)]
    [InlineData(6.25f, 6f, false, false)]
    public void AGroundedBodyWalkingOffADrop_KeepsToTheLowerFloorWithinStepHeight(float drop, float stepHeight, bool oneWay, bool keeps)
    {
        Scene scene = new();
        scene.Add(new Block(new Vector2(0f, GroundTop), new Vector2(64f, 32f)));
        scene.Add(new Block(new Vector2(64f, GroundTop + drop), new Vector2(64f, 16f), oneWay: oneWay));
        SceneFixtures.Body body = Grounded(scene, 48f, stepHeight);
        bool alwaysOnFloor = true;

        for (int step = 0; step < 20; step++)
        {
            body.Mover.Move(new Vector2(1f, Gravity));
            alwaysOnFloor &= body.Mover.IsOnFloor;
        }

        Assert.Equal(keeps, alwaysOnFloor);
        if (keeps)
        {
            Assert.Equal(GroundTop + drop - Edge, body.Position.Y, CollisionFixtures.Tolerance);
            Assert.Equal(-Vector2.UnitY, body.Mover.FloorNormal);
            Assert.Contains(body.Mover.MoveContacts.ToArray(), contact => MathF.Abs(contact.Point.Y - (GroundTop + drop)) < CollisionFixtures.Tolerance);
        }
    }

    // A one-way ledge 4 above the floor is walked through from the side, never stepped onto. A body on
    // it that drops through leaves it, although solid ground lies within StepHeight below.
    [Fact]
    public void AOneWayLip_IsWalkedThroughFromTheSide_AndDropThroughStillDrops()
    {
        Scene scene = new();
        scene.Add(new Block(new Vector2(0f, GroundTop), new Vector2(128f, 16f)));
        scene.Add(new Block(new Vector2(48f, GroundTop - 4f), new Vector2(80f, 2f), oneWay: true));
        SceneFixtures.Body walker = Grounded(scene, 36f, 8f);
        SceneFixtures.Body dropper = Grounded(scene, 100f, 8f);
        dropper.Position = new Vector2(100f, GroundTop - 4f - Edge);
        dropper.Mover.Move(new Vector2(0f, Gravity));
        Assert.True(dropper.Mover.IsOnFloor);

        for (int step = 0; step < 10; step++)
        {
            walker.Mover.Move(new Vector2(Speed, Gravity));
        }

        dropper.Mover.DropThrough();
        dropper.Mover.Move(new Vector2(0f, Gravity));

        Assert.Equal(56f, walker.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(GroundTop - Edge, walker.Position.Y, CollisionFixtures.Tolerance);
        Assert.False(dropper.Mover.IsOnFloor);
        Assert.Equal(GroundTop - 4f - Edge + Gravity, dropper.Position.Y, CollisionFixtures.Tolerance);
    }

    // A lip on a layer the body is moved by is a floor like any other once stepped onto, and carries it.
    [Fact]
    public void ABodySteppingOntoAMovingLip_RidesIt()
    {
        Scene scene = new();
        scene.Add(new Block(new Vector2(0f, GroundTop), new Vector2(128f, 16f)));
        Block lip = new(new Vector2(48f, GroundTop - 4f), new Vector2(16f, 4f), "platform");
        scene.Add(lip);
        SceneFixtures.Body body = Grounded(scene, 36f, 4f);
        body.Mover.MovedBy = new("platform");

        for (int step = 0; step < 8; step++)
        {
            body.Mover.Move(new Vector2(Speed, Gravity));
        }

        Assert.Equal(GroundTop - 4f - Edge, body.Position.Y, CollisionFixtures.Tolerance);
        lip.Position += new Vector2(3f, 0f);
        Assert.Equal(55f, body.Position.X, CollisionFixtures.Tolerance);
    }

    // A 3 lip at the foot of a 1:2 slope, and a 1:2 slope whose top meets a 3 lip onto a flat top. The
    // body steps and walks on without leaving the floor, covering its whole speed across every step.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStep_ComposesWithASlope_AtItsFootOrItsTop(bool lipAtFoot)
    {
        Scene scene = new();
        scene.Add(new Block(new Vector2(0f, GroundTop), new Vector2(160f, 16f)));
        if (lipAtFoot)
        {
            scene.Add(new Block(Vector2.Zero, [new(48f, 64f), new(48f, 61f), new(112f, 29f), new(112f, 64f)]));
        }
        else
        {
            scene.Add(new Block(Vector2.Zero, [new(16f, 64f), new(48f, 48f), new(48f, 64f)]));
            scene.Add(new Block(new Vector2(48f, 45f), new Vector2(112f, 19f)));
        }

        SceneFixtures.Body body = Grounded(scene, lipAtFoot ? 32f : 0f, 4f);

        for (int step = 0; step < 24; step++)
        {
            MoveResult2D result = body.Mover.Move(new Vector2(Speed, Gravity));

            Assert.True(body.Mover.IsOnFloor, $"step {step} left the floor at {body.Position}");
            Assert.Equal(Speed, result.Translation.X, CollisionFixtures.Tolerance);
        }

        if (lipAtFoot)
        {
            Assert.True(body.Position.Y < 61f - Edge - 8f);
        }
        else
        {
            Assert.Equal(45f - Edge, body.Position.Y, CollisionFixtures.Tolerance);
        }
    }

    // One move walks down a slope into a valley, up a second slope on a moving layer, steps 3 onto a
    // solid flat lip and walks on. The floor it reports and the collider it rides are the lip's.
    [Fact]
    public void AMoveThatStepsOffASlope_TakesItsFloorFromTheLanding()
    {
        Scene scene = new();
        scene.Add(new Block(Vector2.Zero, [new(0f, 48f), new(32f, 64f), new(0f, 64f)]));
        Block rising = new(Vector2.Zero, [new(32f, 64f), new(64f, 48f), new(64f, 64f)], "platform");
        scene.Add(rising);
        scene.Add(new Block(new Vector2(64f, 45f), new Vector2(96f, 19f)));
        SceneFixtures.Body body = new(new Vector2(4f, 32f), blocksOn: "solid");
        body.Mover.Mode = BodyMode.Grounded;
        body.Mover.StepHeight = 4f;
        body.Mover.MovedBy = new("platform");
        scene.Add(body);
        body.Mover.Move(new Vector2(0f, 40f));
        Assert.True(body.Mover.IsOnFloor);

        body.Mover.Move(new Vector2(60f, Gravity));

        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(64f, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(45f - Edge, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.Equal(-Vector2.UnitY, body.Mover.FloorNormal);
        rising.Position += new Vector2(3f, 0f);
        Assert.Equal(64f, body.Position.X, CollisionFixtures.Tolerance);
    }

    // Lands a grounded 8x8 body standing on the ground at `x`.
    private static SceneFixtures.Body Grounded(Scene scene, float x, float stepHeight)
    {
        SceneFixtures.Body body = new(new Vector2(x, GroundTop - Edge - 1f), blocksOn: "solid");
        body.Mover.Mode = BodyMode.Grounded;
        body.Mover.StepHeight = stepHeight;
        scene.Add(body);
        body.Mover.Move(new Vector2(0f, 40f));
        Assert.True(body.Mover.IsOnFloor);

        return body;
    }

    // Ground on the fourth row of 16 tiles, with a tile at x = 48 that is solid in its bottom `lip`.
    private static Scene LipGrid(float lip)
    {
        int[] cells =
        [
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 2, 0, 0, 0, 0,
            1, 1, 1, 1, 1, 1, 1, 1,
        ];
        float top = SceneFixtures.TileSize - lip;
        TileGrid grid = new(
            SceneFixtures.TileSize,
            8,
            5,
            [
                TileGrid.EmptyTile,
                new TileType { Name = "solid", Cell = 0, Layer = "solid" },
                new TileType { Name = "lip", Cell = 0, Layer = "solid", Shape = Shape2D.Polygon([new(0f, top), new(16f, top), new(16f, 16f), new(0f, 16f)]) },
            ],
            cells,
            SceneFixtures.Atlas,
            1);

        return new Scene(SceneFixtures.Content(
            new SceneDocument([new TileMapPlacement(SceneFixtures.TerrainId, grid)], SceneFixtures.TerrainId + 1),
            SceneFixtures.Registry()));
    }

    // A box or a polygon on a layer, "solid" unless named.
    private sealed class Block : Entity
    {
        internal Block(Vector2 position, Vector2 size, string layer = "solid", bool oneWay = false)
            : base(position) =>
            Add(new BoxCollider2D(size) { Layer = layer, OneWay = oneWay });

        internal Block(Vector2 position, Vector2[] points, string layer = "solid")
            : base(position) =>
            Add(new PolygonCollider2D(points) { Layer = layer });
    }
}

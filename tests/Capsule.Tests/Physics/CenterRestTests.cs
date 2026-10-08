using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

// A grounded body's RestsOnCenter: its 8x8 box stands with its bottom center on the floor, up to 4 units
// below the pose it sweeps at the default MaxFloorAngle.
public sealed class CenterRestTests
{
    private const float Speed = 2f;

    private const float Gravity = 0.5f;

    private const float Edge = 8f;

    private const float Allowance = 4f;

    // A walk and a jump on a flat floor put a body at exactly the place it reaches with the mode off.
    [Fact]
    public void OnAFlatFloor_ABodyMovesExactlyAsItWouldWithTheModeOff()
    {
        Scene scene = SceneFixtures.Terrain("................", "................", "################").Started();
        SceneFixtures.Body resting = Rest(scene, new Vector2(40f, 0f));
        SceneFixtures.Body plain = Rest(scene, new Vector2(40f, 0f), restsOnCenter: false);

        for (int step = 0; step < 60; step++)
        {
            Vector2 move = new(step < 30 ? Speed : -Speed, step == 20 ? -6f : Gravity);
            Assert.Equal(plain.Mover.Move(move), resting.Mover.Move(move));
            Assert.Equal(plain.Position, resting.Position);
            Assert.Equal(plain.Mover.FloorNormal, resting.Mover.FloorNormal);
        }
    }

    // A tile slope, a 1:2 polygon face and the peak between two faces. The landing move itself puts the
    // bottom center on the surface below it, and FloorNormal is that surface's normal.
    [Theory]
    [InlineData(0, 20f)]
    [InlineData(1, 36f)]
    [InlineData(2, 61f)]
    public void ABodyLandingOnAnUnevenFloor_StandsWithItsBottomCenterOnItInThatMove(int floor, float x)
    {
        Scene scene = (floor == 0 ? SceneFixtures.Terrain("....", "./..", "####") : new Scene()).Started();
        if (floor > 0)
        {
            scene.Add(floor == 1
                ? new Block([new(0f, 64f), new(64f, 32f), new(64f, 64f)])
                : new Block([new(32f, 64f), new(64f, 48f), new(96f, 64f)]));
        }

        SceneFixtures.Body body = Rest(scene, new Vector2(x, 0f));

        AssertOnCenterFloor(scene, body);
        Assert.True(scene.Collision.Raycast(Center(body) - new Vector2(0f, 64f), Vector2.UnitY, 128f, Solid(scene), out RayHit2D ground));
        Assert.Equal(ground.Normal, body.Mover.FloorNormal);
    }

    // A slope falls to the right into a wall standing at its foot, upright or leaning back at 60 degrees.
    // The box's hanging half reaches the leaning face first, and the wall stops the whole box there.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWallAtTheFootOfASlope_StopsTheWholeBox(bool leaning)
    {
        Scene scene = new Scene().Started();
        scene.Add(new Block([new(0f, 16f), new(48f, 64f), new(0f, 64f)]));
        scene.Add(new Block([new(0f, 64f), new(160f, 64f), new(160f, 80f), new(0f, 80f)]));
        scene.Add(leaning
            ? new Block([new(48f, 64f), new(48f + (32f / MathF.Sqrt(3f)), 32f), new(80f, 32f), new(80f, 64f)], "wall")
            : new Block([new(48f, 32f), new(80f, 32f), new(80f, 64f), new(48f, 64f)], "wall"));
        SceneFixtures.Body body = Rest(scene, new Vector2(12f, 0f));

        for (int step = 0; step < 30; step++)
        {
            body.Mover.Move(new Vector2(Speed, Gravity));
            AssertClear(scene, body, 0f, "wall");
        }

        Assert.True(body.Mover.IsOnWall);
        Assert.True(body.Mover.IsOnFloor);
    }

    // A body landing on a slope beside a 4 unit lip at its foot stands clear of the lip, and walks on over it.
    [Fact]
    public void ABodyLandingBesideALipAtTheFootOfASlope_StandsClearOfIt()
    {
        Scene scene = new Scene().Started();
        scene.Add(new Block([new(0f, 16f), new(48f, 64f), new(0f, 64f)]));
        scene.Add(new Block([new(48f, 60f), new(80f, 60f), new(80f, 64f), new(48f, 64f)], "wall"));
        SceneFixtures.Body body = Rest(scene, new Vector2(42f, 0f));

        for (int step = 0; step < 10; step++)
        {
            AssertClear(scene, body, 0f, "wall");
            body.Mover.Move(new Vector2(Speed, Gravity));
        }

        Assert.True(body.Mover.IsOnFloor);
    }

    // A 2 unit ledge between flat floors. Walking off it, the box keeps the ledge's height until the
    // whole box has left it, even off a one-way ledge whose side no query meets. Stepping up onto a
    // solid ledge, the box stands on it as soon as it has climbed.
    [Theory]
    [InlineData(1f, false)]
    [InlineData(-1f, false)]
    [InlineData(1f, true)]
    public void AtALedge_TheBoxNeverSinksIntoIt(float way, bool oneWay)
    {
        Scene scene = new Scene().Started();
        scene.Add(new Block([new(0f, 62f), new(64f, 62f), new(64f, 80f), new(0f, 80f)], oneWay: oneWay));
        scene.Add(new Block([new(64f, 64f), new(128f, 64f), new(128f, 80f), new(64f, 80f)]));
        SceneFixtures.Body body = Rest(scene, new Vector2(way > 0f ? 44f : 76f, 0f));
        body.Mover.StepHeight = 4f;

        for (int step = 0; step < 16; step++)
        {
            body.Mover.Move(new Vector2(way * Speed, Gravity));
            AssertClear(scene, body, 0f);
        }

        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(way > 0f ? 64f : 62f, body.Position.Y + Edge, CollisionFixtures.Tolerance);
    }

    // Right and back over flat ground, a hill of 45 degree tiles, a peak, a V valley, and then a 1:2 ramp
    // of two polygon colliders ending at a 4 unit ledge. The box never ends a step below the floor under
    // its center or deeper than the allowance into anything. On the floor it covers its whole speed
    // across and moves no further up or down than a step and one step's climb.
    [Fact]
    public void ALongWalkOverEveryTransition_KeepsTheCenterOnTheFloorAndTheBoxWithinTheAllowance()
    {
        Scene scene = SceneFixtures.Terrain(
            new string('.', 50),
            new string('.', 50),
            "....../#\\......./\\".PadRight(50, '.'),
            "...../###\\...../##\\..../###\\/###\\".PadRight(50, '.'),
            new string('#', 50)).Started();
        scene.Add(new Block([new(640f, 64f), new(672f, 48f), new(680f, 48f), new(680f, 64f)]));
        scene.Add(new Block([new(680f, 48f), new(704f, 60f), new(704f, 64f), new(680f, 64f)]));
        SceneFixtures.Body body = Rest(scene, new Vector2(8f, 0f));
        body.Mover.StepHeight = 4f;
        float fall = 0f;
        float height = body.Position.Y;

        for (int step = 0; step < 720; step++)
        {
            float way = step < 360 ? 1f : -1f;
            fall += Gravity;
            MoveResult2D result = body.Mover.Move(new Vector2(way * Speed, fall));
            if (body.Mover.IsOnFloor)
            {
                fall = 0f;
                Assert.Equal(Speed, MathF.Abs(result.Translation.X), 0.02f);
                Assert.True(MathF.Abs(body.Position.Y - height) <= body.Mover.StepHeight + Speed, $"step {step} popped to {body.Position}");
            }

            height = body.Position.Y;

            Assert.True(scene.Collision.Raycast(Center(body) - new Vector2(0f, 64f), Vector2.UnitY, 128f, Solid(scene), out RayHit2D ground));
            Assert.True(Center(body).Y <= ground.Point.Y + CollisionFixtures.Tolerance, $"step {step} sank to {body.Position}");
            AssertClear(scene, body, Allowance);
        }

        Assert.True(body.Mover.IsOnFloor);
        Assert.Equal(8f, body.Position.X, 0.5f);
    }

    // Right or left over a tile peak, a tile V valley on a plateau and a V valley of two polygon
    // colliders. Every step ends on the floor and covers its whole speed across. With the mode on, the
    // bottom center stands on the floor under it. With it off, the box's bottom rests on the highest
    // ground under it, never floating more than the contact skin above it.
    [Theory]
    [InlineData(1f, false)]
    [InlineData(-1f, false)]
    [InlineData(1f, true)]
    [InlineData(-1f, true)]
    public void OverCrestsAndValleys_TheBoxKeepsToTheGroundEveryStep(float way, bool restsOnCenter)
    {
        Scene scene = SceneFixtures.Terrain(
            new string('.', 24),
            new string('.', 24),
            "..../\\./#\\/#..#\\........",
            new string('#', 24)).Started();
        scene.Add(new Block([new(192f, 32f), new(208f, 48f), new(192f, 48f)]));
        scene.Add(new Block([new(208f, 48f), new(224f, 32f), new(224f, 48f)]));
        SceneFixtures.Body body = Rest(scene, new Vector2(way > 0f ? 40f : 300f, 0f), restsOnCenter);
        CollisionFilter solid = Solid(scene);

        for (int step = 0; step < 130; step++)
        {
            MoveResult2D result = body.Mover.Move(new Vector2(way * Speed, Gravity));

            Assert.True(body.Mover.IsOnFloor, $"step {step} left the floor at {body.Position}");
            Assert.True(MathF.Abs(MathF.Abs(result.Translation.X) - Speed) <= 0.02f, $"step {step} covered {result.Translation.X} across at {body.Position}");
            float ground = float.PositiveInfinity;
            float bottom = body.Position.Y + Edge;
            if (restsOnCenter)
            {
                Assert.True(scene.Collision.Raycast(Center(body) - new Vector2(0f, 64f), Vector2.UnitY, 128f, solid, out RayHit2D hit));
                ground = hit.Point.Y;
            }
            else
            {
                for (float x = body.Position.X; x <= body.Position.X + Edge; x += 0.25f)
                {
                    Assert.True(scene.Collision.Raycast(new Vector2(x, -64f), Vector2.UnitY, 128f, solid, out RayHit2D hit));
                    ground = MathF.Min(ground, hit.Point.Y);
                }
            }

            Assert.True(ground - bottom >= -CollisionFixtures.Tolerance && ground - bottom <= CollisionTolerance.ContactSkin, $"step {step} stands {ground - bottom} off the ground at {body.Position}");
        }
    }

    // A rise smaller than the sink still leaves the floor. The sink takes it first, and the entity rises
    // by exactly the rise asked for.
    [Fact]
    public void ARiseSmallerThanTheSink_LeavesTheFloor()
    {
        Scene scene = SceneFixtures.Terrain("....", "./..", "####").Started();
        SceneFixtures.Body body = Rest(scene, new Vector2(20f, 0f));
        Vector2 standing = body.Position;

        body.Mover.Move(new Vector2(0f, -1f));

        Assert.False(body.Mover.IsOnFloor);
        Assert.Equal(standing.Y - 1f, body.Position.Y, 1e-4f);
    }

    // A one-way slope holds the center on its face, and DropThrough still drops the body through it.
    [Fact]
    public void OnAOneWaySlope_TheCenterRestsOnIt_AndDropThroughStillDrops()
    {
        Scene scene = new Scene().Started();
        scene.Add(new Block([new(0f, 64f), new(64f, 32f), new(64f, 64f)], oneWay: true));
        SceneFixtures.Body body = Rest(scene, new Vector2(36f, 0f));
        AssertOnCenterFloor(scene, body);

        body.Mover.DropThrough();
        for (int step = 0; step < 20; step++)
        {
            body.Mover.Move(new Vector2(0f, 4f));
        }

        Assert.True(body.Position.Y > 64f);
    }

    // A sloped platform climbing to the right carries its rider, whose center stays on the face.
    [Fact]
    public void ABodyRidingASlopedPlatform_KeepsItsCenterOnTheFace()
    {
        Scene scene = new Scene().Started();
        Block platform = new([new(0f, 64f), new(64f, 32f), new(64f, 64f)], "platform");
        scene.Add(platform);
        SceneFixtures.Body body = new(new Vector2(36f, 0f), blocksOn: "platform");
        body.Mover.Mode = BodyMode.Grounded;
        body.Mover.RestsOnCenter = true;
        body.Mover.MovedBy = new("platform");
        scene.Add(body);
        body.Mover.Move(new Vector2(0f, 40f));

        for (int step = 0; step < 20; step++)
        {
            platform.Position += new Vector2(1f, -0.5f);
            body.Mover.Move(new Vector2(0f, Gravity));
            AssertOnCenterFloor(scene, body, "platform");
        }

        Assert.Equal(56f, body.Position.X, CollisionFixtures.Tolerance);
    }

    // A pusher crossing only the top of the box the body sweeps, above the lowered entity, shoves the
    // body exactly as it would with the mode off.
    [Fact]
    public void APusherCrossingTheTopOfTheSweptBox_ShovesTheBodyAsWithTheModeOff()
    {
        float[] reached = new float[2];
        for (int mode = 0; mode < 2; mode++)
        {
            Scene scene = new Scene().Started();
            scene.Add(new Block([new(0f, 0f), new(64f, 64f), new(0f, 64f)]));
            Block pusher = new([new(31f, 28.5f), new(35f, 28.5f), new(35f, 29.5f), new(31f, 29.5f)], "pusher");
            scene.Add(pusher);
            SceneFixtures.Body body = Rest(scene, new Vector2(36f, 0f), restsOnCenter: mode == 1);
            body.Mover.MovedBy = new("pusher");

            for (int step = 0; step < 4; step++)
            {
                pusher.Position += new Vector2(Speed, 0f);
            }

            reached[mode] = body.Position.X;
        }

        Assert.Equal(43f, reached[0], CollisionFixtures.Tolerance);
        Assert.Equal(reached[0], reached[1], CollisionFixtures.Tolerance);
    }

    // A position the game sets clears the sink. The body then sweeps from where the game put it, and a
    // ceiling 2 units above that place does not stop a 1.5 unit rise.
    [Fact]
    public void SettingThePosition_ClearsTheSink()
    {
        Scene scene = SceneFixtures.Terrain("........", "./......", "########").Started();
        scene.Add(new Block([new(64f, 0f), new(128f, 0f), new(128f, 18f), new(64f, 18f)]));
        SceneFixtures.Body body = Rest(scene, new Vector2(20f, 0f));
        AssertOnCenterFloor(scene, body);

        body.Position = new Vector2(96f, 20f);

        Assert.False(body.Mover.TestMove(new Vector2(0f, -1.5f)));
    }

    private static CollisionFilter Solid(Scene scene) => scene.Collision.CreateFilter("solid");

    private static Vector2 Center(SceneFixtures.Body body) => body.Position + new Vector2(Edge / 2f, Edge);

    private static void AssertOnCenterFloor(Scene scene, SceneFixtures.Body body, string layer = "solid")
    {
        Assert.True(scene.Collision.Raycast(Center(body) - new Vector2(0f, 64f), Vector2.UnitY, 128f, scene.Collision.CreateFilter(layer), out RayHit2D ground));
        Assert.Equal(ground.Point.Y, Center(body).Y, CollisionTolerance.ContactSkin);
    }

    // Asserts the box, raised by `allowance`, overlaps nothing on `layer` by more than the tolerance.
    private static void AssertClear(Scene scene, SceneFixtures.Body body, float allowance, string layer = "solid")
    {
        float inset = CollisionFixtures.Tolerance;
        Aabb2D box = CollisionFixtures.Box(body.Position.X + inset, body.Position.Y - allowance + inset, Edge - (2f * inset), Edge - (2f * inset));
        int overlaps = scene.Collision.OverlapBoxAll(box, scene.Collision.CreateFilter(layer), default);
        Assert.True(overlaps == 0, $"the box at {body.Position} overlaps {overlaps} surfaces");
    }

    // Lands a grounded 8x8 body with RestsOnCenter, blocked by solid and wall, from where it starts.
    private static SceneFixtures.Body Rest(Scene scene, Vector2 position, bool restsOnCenter = true)
    {
        SceneFixtures.Body body = new(position);
        body.Mover.BlockedBy = new("solid", "wall");
        body.Mover.Mode = BodyMode.Grounded;
        body.Mover.RestsOnCenter = restsOnCenter;
        scene.Add(body);
        body.Mover.Move(new Vector2(0f, 80f));
        Assert.True(body.Mover.IsOnFloor);

        return body;
    }

    // A convex polygon collider.
    private sealed class Block : Entity
    {
        internal Block(Vector2[] points, string layer = "solid", bool oneWay = false)
            : base(Vector2.Zero) =>
            Add(new PolygonCollider2D(points) { Layer = layer, OneWay = oneWay });
    }
}

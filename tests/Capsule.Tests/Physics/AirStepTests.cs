using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

// A grounded body's AirStepHeight: stepping up onto a lip while off the floor or rising.
public sealed class AirStepTests
{
    private const float Speed = 2f;

    private const float Gravity = 0.5f;

    private const float AirStep = 8f;

    // The ledge's face is at x = 48 and its top at y = 48. The body stands just clear of the face.
    private const float Face = 48f;

    private const float LedgeTop = 48f;

    private const float Edge = 8f;

    private const float StartX = Face - Edge - CollisionTolerance.LinearSlop;

    [Fact]
    public void AirStepHeight_RejectsANegativeHeight()
    {
        KinematicBody2D body = new(new BoxCollider2D(new Vector2(Edge, Edge)));

        Assert.Throws<ArgumentOutOfRangeException>(() => body.AirStepHeight = -1f);
    }

    // A body off the floor whose feet are `below` under the ledge's top moves across into its face by
    // `rise` up. It climbs onto the ledge when the ledge is within AirStepHeight and there is room above
    // to rise, in either mode of resting. A fall or a level move ends on the ledge, and a rise goes on
    // from it, off the floor and off the wall. Otherwise the face stops it as with AirStepHeight 0, as it
    // does a move that follows DropThrough and a floating body.
    [Theory]
    [InlineData(8f, Gravity, 0f, false, false, false, true)]
    [InlineData(8.25f, Gravity, 0f, false, false, false, false)]
    [InlineData(4f, 0f, 0f, false, false, false, true)]
    [InlineData(8f, -3f, 0f, false, false, false, true)]
    [InlineData(8.25f, -3f, 0f, false, false, false, false)]
    [InlineData(6f, Gravity, 0f, true, false, false, true)]
    [InlineData(6f, -3f, 0f, true, false, false, true)]
    [InlineData(4f, Gravity, 2f, false, false, false, false)]
    [InlineData(4f, Gravity, 0f, false, true, false, false)]
    [InlineData(4f, Gravity, 0f, false, false, true, false)]
    public void ABodyOffTheFloorMovingIntoALedge_ClimbsItWithinAirStepHeightAndHeadroom_OrIsStoppedByIt(
        float below, float rise, float headroom, bool restsOnCenter, bool through, bool floating, bool climbs)
    {
        Scene scene = Ledge(oneWay: false);
        float startY = LedgeTop + below - Edge;
        if (headroom > 0f)
        {
            scene.Add(new Block(Vector2.Zero, new Vector2(128f, startY - headroom)));
        }

        SceneFixtures.Body body = Airborne(scene, startY, mover =>
        {
            mover.RestsOnCenter = restsOnCenter;
            mover.Mode = floating ? BodyMode.Floating : BodyMode.Grounded;
        });
        if (through)
        {
            body.Mover.DropThrough();
        }

        body.Mover.Move(new Vector2(Speed, rise));

        Assert.Equal(climbs ? StartX + Speed : StartX, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(climbs ? LedgeTop - Edge + MathF.Min(rise, 0f) : startY + rise, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.Equal(climbs && rise >= 0f, body.Mover.IsOnFloor);
        Assert.Equal(!climbs, body.Mover.IsOnWall);
    }

    // A one-way ledge blocks only from above. A body off the floor moves through its side and is never
    // lifted onto it.
    [Fact]
    public void AOneWayLedge_IsMovedThroughFromTheSide()
    {
        Scene scene = Ledge(oneWay: true);
        float startY = LedgeTop + 4f - Edge;
        SceneFixtures.Body body = Airborne(scene, startY);

        body.Mover.Move(new Vector2(Speed, Gravity));

        Assert.Equal(StartX + Speed, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(startY + Gravity, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.False(body.Mover.IsOnFloor);
    }

    // A body resting on its center stands sunk on a 1:2 slope that runs down into a ledge 4 above its
    // swept pose. A rise smaller than the sink still lifts it onto the ledge and on by the whole rise,
    // off the floor and with no sink left.
    [Fact]
    public void ARisingStepFromASink_RisesItsWholeRiseFromTheLedge()
    {
        Scene scene = new Scene().Started();
        scene.Add(new Block([new(0f, 40f), new(Face, 64f), new(0f, 64f)]));
        scene.Add(new Block(new Vector2(Face, 56f), new Vector2(32f, 24f)));
        SceneFixtures.Body body = CollisionFixtures.Grounded(scene, new Vector2(StartX, 40f), setUp: mover =>
        {
            mover.RestsOnCenter = true;
            mover.AirStepHeight = AirStep;
        });
        Assert.True(body.Position.Y + Edge > 61f, $"the body stands unsunk at {body.Position}");

        body.Mover.Move(new Vector2(Speed, -1f));

        Assert.True(body.Position.X > Face - Edge, $"the body never climbed, at {body.Position}");
        Assert.Equal(56f - Edge - 1f, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.False(body.Mover.IsOnFloor);
        Assert.False(body.Mover.IsOnWall);
    }

    // A walk from the floor that does not rise steps by StepHeight alone. With StepHeight 0 a lip within
    // AirStepHeight stops it.
    [Fact]
    public void AWalkFromTheFloor_IgnoresAirStepHeight()
    {
        Scene scene = new Scene().Started();
        scene.Add(new Block(new Vector2(0f, 64f), new Vector2(128f, 16f)));
        scene.Add(new Block(new Vector2(Face, 60f), new Vector2(16f, 4f)));
        SceneFixtures.Body body = CollisionFixtures.Grounded(scene, new Vector2(StartX, 55f), setUp: mover => mover.AirStepHeight = AirStep);

        body.Mover.Move(new Vector2(Speed, Gravity));

        Assert.True(body.Mover.IsOnWall);
        Assert.Equal(StartX, body.Position.X, CollisionFixtures.Tolerance);
    }

    // A ledge from x = 48 to 80 whose top is y = 48, solid down to the scene's bottom or one-way.
    private static Scene Ledge(bool oneWay)
    {
        Scene scene = new Scene().Started();
        scene.Add(new Block(new Vector2(Face, LedgeTop), new Vector2(32f, oneWay ? 2f : 64f), oneWay: oneWay));

        return scene;
    }

    // An 8x8 body with AirStepHeight set, its corner at `y` just left of the ledge's face, with no floor
    // under it.
    private static SceneFixtures.Body Airborne(Scene scene, float y, Action<KinematicBody2D>? setUp = null)
    {
        SceneFixtures.Body body = new(new Vector2(StartX, y), blocksOn: CollisionFixtures.Solid);
        body.Mover.Mode = BodyMode.Grounded;
        body.Mover.AirStepHeight = AirStep;
        setUp?.Invoke(body.Mover);
        scene.Add(body);

        return body;
    }
}

using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

// MoveResult2D.Correction: what each forgiveness lever of a grounded body adds to a move, and what adds nothing.
public sealed class MoveCorrectionTests
{
    public enum Lever
    {
        StepUp,
        StepDown,
        AirStep,
        RisingStepFromASink,
        CeilingCorner,
        SlopeAndSettle,
    }

    // Each lever's move reports what the lever added as its Correction, and the translation less that
    // correction is the move the body made without it. A slope walk and the settle onto the center add
    // nothing.
    [Theory]
    [InlineData(Lever.StepUp, 0f, -4f)]
    [InlineData(Lever.StepDown, 0f, 4f)]
    [InlineData(Lever.AirStep, 0f, -4f)]
    [InlineData(Lever.RisingStepFromASink, 0f, float.NaN)]
    [InlineData(Lever.CeilingCorner, -3f, 0f)]
    [InlineData(Lever.SlopeAndSettle, 0f, 0f)]
    public void AMove_ReportsWhatItsForgivenessAdded(Lever lever, float x, float y)
    {
        Scene scene = new Scene().Started();
        (SceneFixtures.Body body, Vector2 move, Vector2? expected) = Arrange(scene, lever);

        MoveResult2D result = body.Mover.Move(move);
        Vector2 plain = expected ?? result.Translation;

        Assert.Equal(plain.X, result.Translation.X - result.Correction.X, CollisionFixtures.Tolerance);
        Assert.Equal(plain.Y, result.Translation.Y - result.Correction.Y, CollisionFixtures.Tolerance);
        Assert.Equal(x, result.Correction.X, CollisionFixtures.Tolerance);
        if (float.IsNaN(y))
        {
            // The step lifts the entity by more than the ledge stands above the swept pose, by the sink it let go of.
            Assert.True(result.Correction.Y < -4f, $"the correction {result.Correction} left out the sink");
        }
        else
        {
            Assert.Equal(y, result.Correction.Y, 2f * CollisionFixtures.Tolerance);
        }
    }

    private const float Lip = 4f;

    private const float Face = 48f;

    private const float StartX = Face - 8f - CollisionTolerance.LinearSlop;

    // The body, the move it makes and what that move would do without forgiveness, or null where the move
    // has none to take out.
    private static (SceneFixtures.Body Body, Vector2 Move, Vector2? Plain) Arrange(Scene scene, Lever lever) => lever switch
    {
        Lever.StepUp => OnALip(scene, up: true),
        Lever.StepDown => OnALip(scene, up: false),
        Lever.AirStep => BesideALedge(scene),
        Lever.RisingStepFromASink => SunkBelowALedge(scene),
        Lever.CeilingCorner => UnderACeilingsEdge(scene),
        _ => DownASlope(scene),
    };

    // A floor at y = 64 with a lip 4 tall on it from x = 48 to 80. The body walks up onto it or off its far end.
    private static (SceneFixtures.Body, Vector2, Vector2?) OnALip(Scene scene, bool up)
    {
        scene.Add(new Block(new Vector2(0f, 64f), new Vector2(128f, 16f)));
        scene.Add(new Block(new Vector2(Face, 64f - Lip), new Vector2(32f, Lip)));
        SceneFixtures.Body body = CollisionFixtures.Grounded(scene, new Vector2(up ? StartX : 79.5f, 40f), setUp: mover => mover.StepHeight = 5f);
        Vector2 walk = new(up ? 2f : 1f, 0f);

        return (body, walk, walk);
    }

    // A ledge whose top at y = 48 stands 4 above the feet of a body with no floor under it.
    private static (SceneFixtures.Body, Vector2, Vector2?) BesideALedge(Scene scene)
    {
        scene.Add(new Block(new Vector2(Face, 48f), new Vector2(32f, 64f)));
        SceneFixtures.Body body = new(new Vector2(StartX, 48f + Lip - 8f), blocksOn: CollisionFixtures.Solid);
        body.Mover.AirStepHeight = 8f;
        scene.Add(body);

        return (body, new Vector2(2f, 0f), new Vector2(2f, 0f));
    }

    // AirStepTests' sunk body on a 1:2 slope running down into a ledge, rising into it.
    private static (SceneFixtures.Body, Vector2, Vector2?) SunkBelowALedge(Scene scene)
    {
        scene.Add(new Block([new(0f, 40f), new(Face, 64f), new(0f, 64f)]));
        scene.Add(new Block(new Vector2(Face, 56f), new Vector2(32f, 24f)));
        SceneFixtures.Body body = CollisionFixtures.Grounded(scene, new Vector2(StartX, 40f), setUp: mover =>
        {
            mover.RestsOnCenter = true;
            mover.AirStepHeight = 8f;
        });

        return (body, new Vector2(2f, -1f), new Vector2(2f, -1f));
    }

    // CeilingCornerTests' ceiling reaching 3 in from the right over a rising body.
    private static (SceneFixtures.Body, Vector2, Vector2?) UnderACeilingsEdge(Scene scene)
    {
        scene.Add(new Block(new Vector2(45f, 20f), new Vector2(64f, 16f)));
        SceneFixtures.Body body = new(new Vector2(40f, 40f), blocksOn: CollisionFixtures.Solid);
        body.Mover.CeilingCornerCorrection = 3f;
        scene.Add(body);

        return (body, new Vector2(0f, -6f), new Vector2(0f, -6f));
    }

    // A body resting on its center walks down a 1:2 slope, sinking as it goes.
    private static (SceneFixtures.Body, Vector2, Vector2?) DownASlope(Scene scene)
    {
        scene.Add(new Block([new(0f, 40f), new(96f, 88f), new(0f, 88f)]));
        SceneFixtures.Body body = CollisionFixtures.Grounded(scene, new Vector2(24f, 30f), setUp: mover =>
        {
            mover.RestsOnCenter = true;
            mover.StepHeight = 5f;
        });

        return (body, new Vector2(4f, 0f), null);
    }
}

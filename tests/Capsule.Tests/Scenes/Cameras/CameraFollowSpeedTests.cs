using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// MaxFollowSpeed caps how far the follow moves the view each step on each axis. At 60 steps a second
// 480 units a second is 8 a step and 360 is 6.
public sealed class CameraFollowSpeedTests
{
    private static readonly Rect OneScreen = SceneFixtures.OneScreen;

    // A jump of 100 across and 30 down is reached 8 units a step across, landing on the thirteenth step
    // without passing it. The uncapped vertical locks on at once.
    [Fact]
    public void ASubjectThatJumps_IsReachedAtTheCappedRateOnACappedAxis_AndLockedOnToOnAFreeOne()
    {
        EntityHierarchyFixtures.Node subject = new(Vector2.Zero);
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Following(subject, c => c.MaxFollowSpeed = new Vector2(480f, 0f));
        simulation.Step(SceneFixtures.Step(0));

        subject.Position = new Vector2(100f, 30f);
        for (int step = 1; step <= 13; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
            Assert.Equal(MathF.Min(step * 8f, 100f), camera.Center.X, 1e-3f);
            Assert.Equal(30f, camera.Center.Y);
        }

        Assert.Equal(subject.Position, camera.Center);
    }

    // A cap of one unit a step would leave a subject 1000 units away off screen. The frame's right edge
    // holds it instead.
    [Fact]
    public void TheHardEdge_OutranksTheCap_AndKeepsTheSubjectInTheFrame()
    {
        EntityHierarchyFixtures.Node subject = new(Vector2.Zero);
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Following(subject, c => c.MaxFollowSpeed = new Vector2(60f, 60f));
        simulation.Step(SceneFixtures.Step(0));

        subject.Position = new Vector2(1000f, 0f);
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(840f, camera.Center.X);
        Assert.Equal(subject.Position.X, camera.VisibleRegion.Right);
    }

    // A Follow and a held subject both leave the follow with no previous step. The cap measures from the
    // view, and the first step back still pans.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewlyFollowedOrResumedSubject_IsPannedTo_RatherThanCutTo(bool resumes)
    {
        EntityHierarchyFixtures.Node first = new(Vector2.Zero);
        EntityHierarchyFixtures.Node second = new(new Vector2(100f, 0f));
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Following(first, c => c.MaxFollowSpeed = new Vector2(480f, 480f));
        first.Scene.Add(second);
        simulation.Step(SceneFixtures.Step(0));

        if (resumes)
        {
            first.StepMode = StepMode.Never;
            simulation.Step(SceneFixtures.Step(1));
            first.Position = new Vector2(100f, 0f);
            first.StepMode = StepMode.Inherit;
        }
        else
        {
            simulation.Step(SceneFixtures.Step(1));
            camera.Follow(second);
        }

        simulation.Step(SceneFixtures.Step(2));

        Assert.Equal(8f, camera.Center.X, 1e-3f);
        Assert.Equal(0f, camera.Center.Y);
    }

    // The view is pinned at 90 while the subject falls 3 units a step and the capped follow reaches 6 past
    // the view each step. That follow centre stands still. The bottom edge opens with no follow move and
    // gains 2 units a step, and the view moves 2. The next step's follow move is the 2 the view made. The
    // view moves 4, then the follow's capped 6. It closes on the subject and tracks it 16 above, at the
    // deadzone's edge, at the subject's 3 a step.
    [Fact]
    public void AnOpeningEdge_UnderACappedFollow_KeepsPaceWithTheCappedMove_AndSettlesOnTheSubject()
    {
        SceneFixtures.Drifter subject = new(new Vector2(160f, 90f), new Vector2(0f, 3f));
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Following(subject, c =>
        {
            c.Deadzone = new Vector2(0f, 32f);
            c.MaxFollowSpeed = new Vector2(0f, 360f);
            c.Bounds = OneScreen;
            c.BoundsTransition = BoundsTransition.AtSpeed(120f);
        });

        for (int step = 0; step < 10; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.Equal(90f, camera.Center.Y);

        camera.Bounds = OneScreen with { Bottom = 10000f };
        float[] moves = [2f, 4f, 6f, 6f, 6f, 6f, 5f, 3f];
        for (int step = 0; step < moves.Length; step++)
        {
            float before = camera.Center.Y;
            simulation.Step(SceneFixtures.Step(10 + step));
            Assert.Equal(moves[step], camera.Center.Y - before, 1e-3f);
        }

        Assert.Equal(subject.Position.Y - 16f, camera.Center.Y, 1e-3f);
    }
}

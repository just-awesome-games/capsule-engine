using System.Numerics;
using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// Bounds settle the camera's own centre, win over the follow, and ease the view into a changed rect.
public sealed class CameraBoundsTests
{
    // One step's blend of a quarter-second ease at 60 steps a second.
    private const float Blend = (1f / 60f) / (0.25f + (1f / 60f));

    private static readonly Rect OneScreen = new(0f, 0f, 320f, 180f);
    private static readonly Rect NextScreen = new(320f, 0f, 640f, 180f);

    // A smoothed centre that ran past the edge would hold the view pinned while it trailed back. The
    // settled centre is the confined one, so the view moves on the step the aim comes back.
    [Fact]
    public void ASmoothedCameraAtAnEdge_MovesOnTheStepItsAimComesBack()
    {
        Still subject = new(new Vector2(160f, 90f));
        (SceneSimulation simulation, Camera camera) = Following(subject, c =>
        {
            c.SmoothTime = 0.25f;
            c.Bounds = new Rect(0f, 0f, 1000f, 180f);
        });

        subject.Position = new Vector2(3000f, 90f);
        for (int step = 0; step < 60; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.Equal(840f, camera.Center.X);

        subject.Position = new Vector2(800f, 90f);
        simulation.Step(SceneFixtures.Step(60));

        Assert.Equal(840f - (40f * Blend), camera.Center.X, 1e-3f);
    }

    // The top edge holds the view at 90 while the subject rises past it and comes back down to 80. The
    // deadzone around the held view spans 74 to 106. Once the edge opens the view stays put, and it moves
    // only when the subject crosses 106. A focus that followed the subject past the edge would sit at 64
    // and pull the view there as the edge opens.
    [Fact]
    public void TheDeadzone_MeasuresFromTheViewTheBoundsHeld_WhenTheSubjectComesBackPastTheirEdge()
    {
        Still subject = new(new Vector2(160f, 90f));
        Rect room = new(0f, 0f, 320f, 1000f);
        (SceneSimulation simulation, Camera camera) = Following(subject, c =>
        {
            c.Deadzone = new Vector2(0f, 32f);
            c.Bounds = room;
        });
        simulation.Step(SceneFixtures.Step(0));

        subject.Position = new Vector2(160f, -200f);
        simulation.Step(SceneFixtures.Step(1));
        subject.Position = new Vector2(160f, 80f);
        simulation.Step(SceneFixtures.Step(2));

        camera.Bounds = room with { Top = float.NegativeInfinity };
        simulation.Step(SceneFixtures.Step(3));
        Assert.Equal(90f, camera.Center.Y);

        subject.Position = new Vector2(160f, 106f);
        simulation.Step(SceneFixtures.Step(4));
        Assert.Equal(90f, camera.Center.Y);

        subject.Position = new Vector2(160f, 110f);
        simulation.Step(SceneFixtures.Step(5));
        Assert.Equal(94f, camera.Center.Y);
    }

    [Fact]
    public void ASubjectLeavingTheBounds_LeavesTheFrame()
    {
        Still subject = new(new Vector2(160f, 90f));
        (SceneSimulation simulation, Camera camera) = Following(subject, c => c.Bounds = new Rect(0f, 0f, 640f, 180f));
        simulation.Step(SceneFixtures.Step(0));

        subject.Position = new Vector2(160f, 400f);
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(OneScreen, camera.VisibleRegion);
    }

    // The next screen's left edge closes from the view's own left edge. The view moves on the first step.
    [Fact]
    public void ChangedBounds_EaseTheViewInFromWhereItStands()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Smooth(0.25f));
        simulation.Step(SceneFixtures.Step(0));

        camera.Bounds = NextScreen;
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(320f * Blend, camera.VisibleRegion.Left, 1e-3f);

        for (int step = 2; step < 300; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.Equal(NextScreen, camera.VisibleRegion);
    }

    // At 120 units a second and 60 steps a second the closing left edge pushes the view 2 units a step
    // from its own edge. It lands on the 160th step. The change keeps its speed after the standing
    // transition turns to a snap.
    [Fact]
    public void AtSpeed_PushesTheViewAtConstantSpeed_AndLandsOnTheRect()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.AtSpeed(120f));
        simulation.Step(SceneFixtures.Step(0));

        camera.Bounds = NextScreen;
        for (int step = 1; step < 160; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
            camera.BoundsTransition = BoundsTransition.Snap;
            Assert.Equal(2f * step, camera.VisibleRegion.Left, 1e-3f);
        }

        simulation.Step(SceneFixtures.Step(160));
        Assert.Equal(NextScreen, camera.VisibleRegion);
    }

    // A smoothed chase starts 20 units a step toward the next screen and ends slower than the cap. Flipping
    // back and forth mid-way never moves the view faster than the cap, with or without the smoothing.
    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    public void ACappedChase_NeverMovesTheViewFasterThanItsMaxSpeed_AcrossARecrossing(float smoothTime)
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Smooth(smoothTime, 120f));
        simulation.Step(SceneFixtures.Step(0));

        int tick = 1;
        foreach ((Rect bounds, int steps) in new[] { (NextScreen, 20), (OneScreen, 10), (NextScreen, 400) })
        {
            camera.Bounds = bounds;
            for (int step = 0; step < steps; step++)
            {
                float before = camera.VisibleRegion.Left;
                simulation.Step(SceneFixtures.Step(tick++));
                Assert.InRange(MathF.Abs(camera.VisibleRegion.Left - before), 0f, 2f + 1e-4f);
            }
        }

        Assert.Equal(NextScreen, camera.VisibleRegion);
    }

    // The view is pinned at 90 while the subject falls 5 units a step, 16 past the deadzone's reach. Once
    // the bottom opens the view falls with the subject and gains 2 units a step on it. It moves 7 a step
    // for 17 steps until the subject is back at the deadzone's edge, then tracks it at 5.
    [Fact]
    public void AnOpeningEdge_KeepsPaceWithAFallingSubject_AndGainsOnItAtTheMaxSpeed()
    {
        Faller subject = new(new Vector2(160f, 90f));
        (SceneSimulation simulation, Camera camera) = Following(subject, c =>
        {
            c.Deadzone = new Vector2(0f, 32f);
            c.Bounds = OneScreen;
            c.BoundsTransition = BoundsTransition.AtSpeed(120f);
        });

        for (int step = 0; step < 10; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.Equal(90f, camera.Center.Y);

        camera.Bounds = OneScreen with { Bottom = 10000f };
        for (int step = 10; step < 30; step++)
        {
            float before = camera.Center.Y;
            simulation.Step(SceneFixtures.Step(step));
            Assert.Equal(step <= 26 ? 7f : 5f, camera.Center.Y - before, 1e-3f);
        }

        Assert.Equal(subject.Position.Y - 16f, camera.Center.Y, 1e-3f);
    }

    // A room as wide as the view and one narrower than it, where the view is centred on each rect.
    [Theory]
    [InlineData(320f)]
    [InlineData(200f)]
    public void AFrameBetweenTwoStepsOfAnEase_InterpolatesTheirViews(float width)
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Smooth(0.25f), new Rect(0f, 0f, width, 180f));
        simulation.Step(SceneFixtures.Step(0));
        float before = camera.VisibleRegion.Left;

        camera.Bounds = new Rect(400f, 0f, 400f + width, 180f);
        simulation.Step(SceneFixtures.Step(1));
        float after = camera.VisibleRegion.Left;

        Assert.NotEqual(before, after);
        Assert.Equal(before + ((after - before) * 0.25f), simulation.View.Camera.Resolve(0.25f, Vector2.Zero).Left, 1e-3f);
    }

    // A view zooming out against an edge keeps that edge still at every point between two steps.
    [Fact]
    public void AViewWideningAgainstAnEdge_HoldsTheEdgeBetweenSteps()
    {
        static void ZoomOut(Scene scene, in StepContext context) => scene.Camera.Zoom = 1f - (0.05f * (context.Tick + 1));

        SceneFixtures.HookScene scene = new(
            start: s =>
            {
                SceneFixtures.Open(s, new Vector2(160f, 90f), new Vector2(320f, 180f));
                s.Camera.Bounds = new Rect(0f, 0f, 1000f, 1000f);
            },
            step: ZoomOut);
        SceneSimulation simulation = new(scene);

        for (int step = 0; step < 4; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
            Assert.Equal(0f, simulation.View.Camera.Resolve(0.5f, Vector2.Zero).Left, 1e-3f);
        }
    }

    [Fact]
    public void ChangedBounds_UnderSnap_SnapTheDrawnFrame()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Snap);
        simulation.Step(SceneFixtures.Step(0));

        camera.Bounds = NextScreen;
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(NextScreen, camera.VisibleRegion);
        Assert.Equal(NextScreen, simulation.View.Camera.Resolve(0f, Vector2.Zero));
    }

    // Half a second is thirty steps. Halfway along InQuad the view has come a quarter of the way, and the
    // thirtieth step lands on the rect.
    [Fact]
    public void Eased_MovesTheViewAlongItsCurve_AndLandsOnItsLastStep()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Snap);
        simulation.Step(SceneFixtures.Step(0));

        camera.SetBounds(NextScreen, BoundsTransition.Eased(0.5f, Ease.InQuad));
        for (int step = 1; step <= 15; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.Equal(320f * 0.25f, camera.VisibleRegion.Left, 1e-2f);

        for (int step = 16; step < 30; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.NotEqual(NextScreen, camera.VisibleRegion);
        simulation.Step(SceneFixtures.Step(30));
        Assert.Equal(NextScreen, camera.VisibleRegion);
    }

    // A trigger re-writing the rect the bounds already hold leaves the linear move a third of the way at
    // its tenth step. A new rect takes over under the standing snap.
    [Fact]
    public void SettingBounds_TakesOverAnEasedChange_UnlessItHoldsTheSameRect()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Snap);
        simulation.Step(SceneFixtures.Step(0));
        camera.SetBounds(NextScreen, BoundsTransition.Eased(0.5f, Ease.Linear));
        for (int step = 1; step <= 10; step++)
        {
            camera.Bounds = NextScreen;
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.Equal(320f / 3f, camera.VisibleRegion.Left, 1e-2f);

        Rect third = new(640f, 0f, 960f, 180f);
        camera.Bounds = third;
        simulation.Step(SceneFixtures.Step(11));

        Assert.Equal(third, camera.VisibleRegion);
    }

    // Zero seconds lands on the first step under a standing chase, as does a change before the first step.
    [Theory]
    [InlineData(false, 0f)]
    [InlineData(true, 0.5f)]
    public void Eased_LandsOnTheFirstStep_WhenItHasNoLengthOrTheCameraHasNotSettled(bool beforeFirstStep, float seconds)
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Smooth(0.25f));
        if (!beforeFirstStep)
        {
            simulation.Step(SceneFixtures.Step(0));
        }

        camera.SetBounds(NextScreen, BoundsTransition.Eased(seconds, Ease.Linear));
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(NextScreen, camera.VisibleRegion);
        Assert.Equal(NextScreen, simulation.View.Camera.Resolve(0f, Vector2.Zero));
    }

    // InBack starts by swinging back. An edge opening to infinity must land rather than swing to the
    // opposite infinity.
    [Fact]
    public void Eased_OpensAnEdgeToInfinityAtOnce_OnACurveThatSwingsBack()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Snap);
        simulation.Step(SceneFixtures.Step(0));

        camera.SetBounds(OneScreen with { Top = float.NegativeInfinity }, BoundsTransition.Eased(0.5f, Ease.InBack));
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(OneScreen, camera.VisibleRegion);
    }

    // A one-off snap under a standing chase lands at once, and the next change chases again. The right
    // edge closes from the view's own edge at 640.
    [Fact]
    public void SetBounds_CarriesOneChange_AndLeavesTheStandingTransition()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Smooth(0.25f));
        simulation.Step(SceneFixtures.Step(0));

        camera.SetBounds(NextScreen, BoundsTransition.Snap);
        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(NextScreen, camera.VisibleRegion);
        Assert.Equal(NextScreen, simulation.View.Camera.Resolve(0f, Vector2.Zero));

        camera.Bounds = OneScreen;
        simulation.Step(SceneFixtures.Step(2));
        Assert.Equal(320f - (320f * Blend), camera.VisibleRegion.Left, 1e-3f);
    }

    [Fact]
    public void AnInfiniteEdge_LeavesThatSideOpen()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.Snap);
        camera.Bounds = new Rect(0f, float.NegativeInfinity, 320f, 180f);

        camera.Teleport(new Vector2(1000f, -5000f));
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(new Vector2(160f, -5000f), camera.Center);
    }

    [Fact]
    public void ANaNEdge_IsRefused()
    {
        Camera camera = new();

        Assert.Throws<ArgumentException>(() => camera.Bounds = new Rect(0f, float.NaN, 320f, 180f));
    }

    // A 320x180 camera confined to OneScreen unless given another rect.
    private static (SceneSimulation Simulation, Camera Camera) Pinned(BoundsTransition transition, Rect? bounds = null)
    {
        SceneFixtures.HookScene scene = new(start: s =>
        {
            SceneFixtures.Open(s, new Vector2(160f, 90f), new Vector2(320f, 180f));
            s.Camera.Bounds = bounds ?? OneScreen;
            s.Camera.BoundsTransition = transition;
        });

        return (new SceneSimulation(scene), scene.Camera);
    }

    private static (SceneSimulation Simulation, Camera Camera) Following(Entity subject, Action<Camera> configure)
    {
        SceneFixtures.HookScene scene = new(start: s =>
        {
            s.Camera.ViewportSize = new Vector2(320f, 180f);
            configure(s.Camera);
            s.Camera.Follow(subject);
        });
        scene.Add(subject);

        return (new SceneSimulation(scene), scene.Camera);
    }

    private sealed class Still(Vector2 position) : Entity(position);

    private sealed class Faller(Vector2 position) : Entity(position)
    {
        protected internal override void OnStep(in StepContext context) => Position += new Vector2(0f, 5f);
    }
}

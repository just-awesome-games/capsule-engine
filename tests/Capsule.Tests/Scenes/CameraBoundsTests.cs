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
        (SceneSimulation simulation, Camera camera) = Pinned(0.25f);
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

    // A room as wide as the view and one narrower than it, where the view is centred on each rect.
    [Theory]
    [InlineData(320f)]
    [InlineData(200f)]
    public void AFrameBetweenTwoStepsOfAnEase_InterpolatesTheirViews(float width)
    {
        (SceneSimulation simulation, Camera camera) = Pinned(0.25f, new Rect(0f, 0f, width, 180f));
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
    public void ChangedBounds_WithNoSmoothTime_SnapTheDrawnFrame()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(0f);
        simulation.Step(SceneFixtures.Step(0));

        camera.Bounds = NextScreen;
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(NextScreen, camera.VisibleRegion);
        Assert.Equal(NextScreen, simulation.View.Camera.Resolve(0f, Vector2.Zero));
    }

    // Half a second is thirty steps. Halfway along InQuad the view has come a quarter of the way, and the
    // thirtieth step lands on the rect.
    [Fact]
    public void EaseBounds_MovesTheViewAlongItsCurve_AndLandsOnItsLastStep()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(0f);
        simulation.Step(SceneFixtures.Step(0));

        camera.EaseBounds(NextScreen, 0.5f, Ease.InQuad);
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

    [Fact]
    public void SettingBounds_TakesOverAnEaseBounds()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(0f);
        simulation.Step(SceneFixtures.Step(0));
        camera.EaseBounds(NextScreen, 0.5f, Ease.Linear);
        for (int step = 1; step <= 10; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Rect third = new(640f, 0f, 960f, 180f);
        camera.Bounds = third;
        simulation.Step(SceneFixtures.Step(11));

        Assert.Equal(third, camera.VisibleRegion);
    }

    // Zero seconds lands on the first step even with the chase on, as does a call before the first step.
    [Theory]
    [InlineData(false, 0f)]
    [InlineData(true, 0.5f)]
    public void EaseBounds_LandsOnTheFirstStep_WhenItHasNoLengthOrTheCameraHasNotSettled(bool beforeFirstStep, float seconds)
    {
        (SceneSimulation simulation, Camera camera) = Pinned(0.25f);
        if (!beforeFirstStep)
        {
            simulation.Step(SceneFixtures.Step(0));
        }

        camera.EaseBounds(NextScreen, seconds, Ease.Linear);
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(NextScreen, camera.VisibleRegion);
        Assert.Equal(NextScreen, simulation.View.Camera.Resolve(0f, Vector2.Zero));
    }

    // InBack starts by swinging back. An edge opening to infinity must land rather than swing to the
    // opposite infinity.
    [Fact]
    public void EaseBounds_OpensAnEdgeToInfinityAtOnce_OnACurveThatSwingsBack()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(0f);
        simulation.Step(SceneFixtures.Step(0));

        camera.EaseBounds(OneScreen with { Top = float.NegativeInfinity }, 0.5f, Ease.InBack);
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(OneScreen, camera.VisibleRegion);
    }

    [Fact]
    public void AnInfiniteEdge_LeavesThatSideOpen()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(0f);
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
    private static (SceneSimulation Simulation, Camera Camera) Pinned(float boundsSmoothTime, Rect? bounds = null)
    {
        SceneFixtures.HookScene scene = new(start: s =>
        {
            SceneFixtures.Open(s, new Vector2(160f, 90f), new Vector2(320f, 180f));
            s.Camera.Bounds = bounds ?? OneScreen;
            s.Camera.BoundsSmoothTime = boundsSmoothTime;
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
}

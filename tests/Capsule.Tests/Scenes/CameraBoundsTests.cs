using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// Bounds settle the camera's own centre, win over the follow, and ease the view into a changed rect.
public sealed class CameraBoundsTests
{
    private static readonly Rect OneScreen = new(0f, 0f, 320f, 180f);
    private static readonly Rect NextScreen = new(320f, 0f, 640f, 180f);

    // One step's blend of a quarter-second ease at 60 steps a second.
    private const float Blend = (1f / 60f) / (0.25f + (1f / 60f));

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

    // The next screen's left edge closes from the view's own left edge, so the view moves on the first
    // step. A frame drawn between two steps interpolates their settled views without clamping.
    [Fact]
    public void ChangedBounds_EaseTheViewInFromWhereItStands_AndAFrameBetweenStepsInterpolates()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(0.25f);
        simulation.Step(SceneFixtures.Step(0));

        camera.Bounds = NextScreen;
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(320f * Blend, camera.VisibleRegion.Left, 1e-3f);
        Assert.Equal(160f * Blend, simulation.View.Camera.Resolve(0.5f, Vector2.Zero).Left, 1e-3f);

        for (int step = 2; step < 300; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.Equal(NextScreen, camera.VisibleRegion);
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

    [Fact]
    public void AnInfiniteEdge_LeavesThatSideOpen()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(0f);
        camera.Bounds = new Rect(0f, float.NegativeInfinity, 320f, 180f);

        camera.Teleport(new Vector2(1000f, -5000f));
        simulation.Step(SceneFixtures.Step(0));

        Assert.Equal(new Vector2(160f, -5000f), camera.Center);
    }

    // A camera centred on OneScreen and confined to it.
    private static (SceneSimulation Simulation, Camera Camera) Pinned(float boundsSmoothTime)
    {
        SceneFixtures.HookScene scene = new(start: s =>
        {
            SceneFixtures.Open(s, new Vector2(160f, 90f), new Vector2(320f, 180f));
            s.Camera.Bounds = OneScreen;
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

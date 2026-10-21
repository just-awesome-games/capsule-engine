using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// Follow, zoom and offset, read back through the settled region the frame draws.
public sealed class CameraKitTests
{
    private static readonly Rect OneScreen = SceneFixtures.OneScreen;

    [Fact]
    public void ASubjectInsideTheDeadzone_HoldsTheCamera_AndLeavingItMovesTheCameraByTheExcess()
    {
        EntityHierarchyFixtures.Node subject = new(Vector2.Zero);
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Following(subject, c => c.Deadzone = new Vector2(40f, 40f));
        simulation.Step(SceneFixtures.Step());

        subject.Position = new Vector2(15f, -18f);
        simulation.Step(SceneFixtures.Step());
        Assert.Equal(Vector2.Zero, camera.Center);

        subject.Position = new Vector2(30f, -18f);
        simulation.Step(SceneFixtures.Step());
        Assert.Equal(new Vector2(10f, 0f), camera.Center);
    }

    [Fact]
    public void ALookaheadEqualToTheSmoothTime_CancelsTheTrailAtASteadySpeed()
    {
        SceneFixtures.Drifter subject = new(Vector2.Zero);
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Following(subject, c =>
        {
            c.SmoothTime = 0.25f;
            c.Lookahead = new Vector2(0.25f, 0.25f);
        });

        for (int step = 0; step < 300; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }

        Assert.Equal(subject.Position.X, camera.Center.X, 1e-2f);
        Assert.Equal(subject.Position.Y, camera.Center.Y);
    }

    // A walk turning each step at 120 units a second stays under a 150 threshold and draws no lead.
    // A drift at 60 units a second past a 20 threshold eases its lead in over SmoothTime. A twin with no
    // lookahead shares every other input. The gap between their centres is the lead after the centre's
    // own smoothing, one blend on the first step where a lead popping in at full size would show none.
    [Fact]
    public void ALookaheadThreshold_HoldsTheLeadAtZeroBelowIt_AndEasesItInPastIt()
    {
        EntityHierarchyFixtures.Node walker = new(Vector2.Zero);
        (SceneSimulation walk, Camera camera) = SceneFixtures.Following(walker, c =>
        {
            c.Lookahead = new Vector2(1f, 1f);
            c.LookaheadThreshold = new Vector2(150f, 150f);
        });

        for (int step = 0; step < 10; step++)
        {
            walker.Position = new Vector2(step % 2 == 0 ? 1f : -1f, 0f);
            walk.Step(SceneFixtures.Step(step));
            Assert.Equal(walker.Position, camera.Center);
        }

        static (SceneSimulation Simulation, Camera Camera) Drifting(float lookahead) =>
            SceneFixtures.Following(new SceneFixtures.Drifter(Vector2.Zero), c =>
            {
                c.SmoothTime = 0.25f;
                c.Lookahead = new Vector2(lookahead, 0f);
                c.LookaheadThreshold = new Vector2(20f, 0f);
            });

        (SceneSimulation leading, Camera leader) = Drifting(0.5f);
        (SceneSimulation trailing, Camera trailer) = Drifting(0f);
        float blend = SceneFixtures.Blend(0.25f);
        float target = (60f - 20f) * 0.5f;
        leading.Step(SceneFixtures.Step(0));
        trailing.Step(SceneFixtures.Step(0));

        Assert.Equal(target * blend * blend, leader.Center.X - trailer.Center.X, 1e-4f);

        for (int step = 1; step < 300; step++)
        {
            leading.Step(SceneFixtures.Step(step));
            trailing.Step(SceneFixtures.Step(step));
        }

        Assert.Equal(target, leader.Center.X - trailer.Center.X, 1e-2f);
    }

    // The cut lands on the aim 17 units above the subject. The deadzone spans 20 either side of that aim,
    // and a rise to -40 moves the view to -37. An aim far above the subject still leaves it on the
    // frame's bottom edge.
    [Fact]
    public void AFollowOffset_MovesTheAimTheDeadzoneMeasuresFrom_AndTheSubjectStaysInTheFrame()
    {
        EntityHierarchyFixtures.Node subject = new(Vector2.Zero);
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Following(subject, c =>
        {
            c.FollowOffset = new Vector2(0f, -17f);
            c.Deadzone = new Vector2(0f, 40f);
        });
        Assert.Equal(new Vector2(0f, -17f), camera.Center);

        subject.Position = new Vector2(0f, -40f);
        simulation.Step(SceneFixtures.Step(0));
        Assert.Equal(new Vector2(0f, -37f), camera.Center);

        camera.FollowOffset = new Vector2(0f, -500f);
        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(subject.Position.Y, camera.VisibleRegion.Bottom);
    }

    // A subject the scene's start moves after the follow, as an arrival moves the player, is framed
    // where it stands once the scene has started. The host draws that frame before the first step.
    [Fact]
    public void AFollowDuringStart_CutsToTheSubjectWhereTheStartLeavesIt_BeforeTheFirstStep()
    {
        EntityHierarchyFixtures.Node subject = new(new Vector2(500f, 300f));
        SceneFixtures.HookScene scene = new(start: s =>
        {
            s.Camera.SmoothTime = 1f;
            s.Camera.Follow(subject);
            subject.Position = new Vector2(-200f, 40f);
        });
        scene.Add(subject);

        SceneSimulation simulation = new(scene);

        Assert.Equal(new Vector2(-200f, 40f), simulation.View.Camera.Center);
        Assert.Equal(simulation.View.Camera.Center, simulation.View.Camera.PreviousCenter);
    }

    // A teleport reports no velocity and the lookahead does not spike. The hard edge alone moves the
    // camera, and the subject lands on the edge of the span the output draws on the same step. A tall
    // output under FixedHeight draws 101.25 units across. On a 320x180 render resolution a 181:320
    // output resolves 101.8125 units but draws 101 whole surface pixels.
    [Theory]
    [InlineData(ViewportFit.Letterbox, 0f, 0f, false, 4840f)]
    [InlineData(ViewportFit.FixedHeight, 180f, 320f, false, 4949.375f)]
    [InlineData(ViewportFit.FixedHeight, 181f, 320f, true, 4949.5f)]
    public void ASubjectTeleportedFarAway_StaysInTheFrameOnTheSameStep(
        ViewportFit fit, float width, float height, bool atRenderResolution, float centerX)
    {
        EntityHierarchyFixtures.Node subject = new(Vector2.Zero);
        Run run = new() { RenderResolution = atRenderResolution ? (320, 180) : null };
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Following(subject, c =>
        {
            c.Fit = fit;
            c.SmoothTime = 1f;
            c.Lookahead = new Vector2(1f, 1f);
        }, run);
        Vector2 output = new(width, height);
        simulation.Step(SceneFixtures.Step(0, output));

        subject.Teleport(new Vector2(5000f, 0f));
        simulation.Step(SceneFixtures.Step(1, output));

        Assert.Equal(new Vector2(centerX, 0f), camera.Center);
        Assert.Equal(subject.WorldPosition.X, camera.VisibleRegion.Right, 1e-3f);
    }

    // A subject added and followed in one step joins the scene after the camera settles.
    [Fact]
    public void ASubjectAddedAndFollowedInOneStep_HoldsTheCameraThatStep_AndIsFollowedFromTheNext()
    {
        EntityHierarchyFixtures.Node focus = new(new Vector2(400f, 50f));

        void AddAndFollow(Scene scene, in StepContext context)
        {
            if (context.Tick == 1)
            {
                scene.Add(focus);
                scene.Camera.Follow(focus);
            }
        }

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero), step: AddAndFollow);
        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step(0));

        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(Vector2.Zero, scene.Camera.Center);

        simulation.Step(SceneFixtures.Step(2));
        Assert.Equal(focus.Position, scene.Camera.Center);
    }

    [Fact]
    public void ASubjectThatLeavesTheScene_StaysTheSubject_AndHoldsTheCameraWhereItLeft()
    {
        EntityHierarchyFixtures.Node focus = new(new Vector2(400f, 50f));
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Following(focus, static _ => { });
        simulation.Step(SceneFixtures.Step(0));

        focus.Scene.Remove(focus);
        focus.Position = new Vector2(900f, 50f);
        simulation.Step(SceneFixtures.Step(1));

        Assert.Same(focus, camera.Subject);
        Assert.Equal(new Vector2(400f, 50f), camera.Center);
    }

    [Fact]
    public void AFirstFollowOfASubjectInAnotherScene_HoldsTheCameraRatherThanCuttingToIt()
    {
        EntityHierarchyFixtures.Node focus = new(new Vector2(400f, 50f));
        using SceneSimulation home = SceneFixtures.Simulation(new Scene(), focus);
        SceneFixtures.HookScene other = new(start: s =>
        {
            SceneFixtures.Open(s, Vector2.Zero, SceneFixtures.Viewport);
            s.Camera.Follow(focus);
        });

        _ = new SceneSimulation(other);

        Assert.Equal(Vector2.Zero, other.Camera.Center);
    }

    [Fact]
    public void TheOffset_MovesTheViewAfterBounds_AndAParallaxLayerByItsFactorOfIt()
    {
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Pinned();
        Vector2 half = new(0.5f, 0.5f);
        simulation.Step(SceneFixtures.Step());
        Vector2 layerBefore = camera.CanvasToWorld(Vector2.Zero, half);

        camera.Offset = new Vector2(10f, -6f);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(Rect.FromEdges(10f, -6f, 330f, 174f), camera.VisibleRegion);
        Assert.Equal(layerBefore + new Vector2(5f, -3f), camera.CanvasToWorld(Vector2.Zero, half));
    }

    // Zoom magnifies the pixels on a declared render resolution rather than shrinking the surface.
    [Fact]
    public void AZoomedView_PlacesTheViewportOverTheZoom_AtThatManySurfacePixelsAUnit()
    {
        static void Zoom(Scene scene, in StepContext context) => scene.Camera.Zoom = context.Tick == 0 ? 2f : 0.5f;

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero), step: Zoom);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step(0));
        Assert.Equal(new Vector2(160f, 90f), scene.Camera.VisibleRegion.Size);

        ScreenLayout layout = FrameLayout.Layout((320, 180), simulation.View.Camera, new Vector2(320f, 180f), 1280, 720);
        Assert.Equal(new Vector2(160f, 90f), layout.Span);
        Assert.Equal(2f, layout.World.Scale);

        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(new Vector2(640f, 360f), scene.Camera.VisibleRegion.Size);
        Assert.Equal(new Vector2(160f, 90f), simulation.View.Camera.PreviousSize);
    }
}

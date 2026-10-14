using System.Numerics;
using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// IsTransitioning reads true exactly while a change of bounds is still carrying the view.
public sealed class CameraBoundsLandingTests
{
    private static readonly Rect OneScreen = new(0f, 0f, 320f, 180f);
    private static readonly Rect NextScreen = new(320f, 0f, 640f, 180f);

    public static TheoryData<string> Transitions => ["snap", "at-speed", "smooth", "eased"];

    // A screen-sized view frames the next screen exactly once its bounds have landed, so the view
    // itself says when the change is over.
    [Theory]
    [MemberData(nameof(Transitions))]
    public void IsTransitioning_ReadsTrueFromTheChangeUntilTheStepTheViewLands(string kind)
    {
        BoundsTransition transition = kind switch
        {
            "at-speed" => BoundsTransition.AtSpeed(120f),
            "smooth" => BoundsTransition.Smooth(0.25f),
            "eased" => BoundsTransition.Eased(0.5f, Ease.InOutSine),
            _ => BoundsTransition.Snap,
        };
        (SceneSimulation simulation, Camera camera) = Pinned(transition);
        simulation.Step(SceneFixtures.Step(0));
        Assert.False(camera.IsTransitioning);

        camera.Bounds = NextScreen;
        Assert.Equal(kind != "snap", camera.IsTransitioning);

        int step = 1;
        for (; step < 600 && camera.VisibleRegion != NextScreen; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
            Assert.Equal(camera.VisibleRegion != NextScreen, camera.IsTransitioning);
        }

        Assert.Equal(NextScreen, camera.VisibleRegion);
        simulation.Step(SceneFixtures.Step(step));
        Assert.False(camera.IsTransitioning);
    }

    [Fact]
    public void ATeleport_LandsARunningChangeAtOnce()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.AtSpeed(120f));
        simulation.Step(SceneFixtures.Step(0));
        camera.Bounds = NextScreen;
        simulation.Step(SceneFixtures.Step(1));
        Assert.True(camera.IsTransitioning);

        camera.Teleport(new Vector2(480f, 90f));
        Assert.False(camera.IsTransitioning);

        simulation.Step(SceneFixtures.Step(2));
        Assert.False(camera.IsTransitioning);
        Assert.Equal(NextScreen, camera.VisibleRegion);
    }

    // A change replaced mid-run carries on toward the new rect, and null bounds end it at once.
    [Fact]
    public void AReplacedChange_ReadsTrueUntilTheNewRectLands_AndNullBoundsEndIt()
    {
        (SceneSimulation simulation, Camera camera) = Pinned(BoundsTransition.AtSpeed(120f));
        simulation.Step(SceneFixtures.Step(0));
        camera.Bounds = NextScreen;
        simulation.Step(SceneFixtures.Step(1));

        camera.Bounds = OneScreen;
        Assert.True(camera.IsTransitioning);
        int step = 2;
        for (; step < 600 && camera.VisibleRegion != OneScreen; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
            Assert.Equal(camera.VisibleRegion != OneScreen, camera.IsTransitioning);
        }

        Assert.Equal(OneScreen, camera.VisibleRegion);
        camera.Bounds = NextScreen;
        simulation.Step(SceneFixtures.Step(step));
        Assert.True(camera.IsTransitioning);

        camera.Bounds = null;
        Assert.False(camera.IsTransitioning);
    }

    // A change before the camera's first settle lands with that settle, and an edge opening to infinity
    // lands on the next one, whatever the transition.
    [Fact]
    public void AChangeBeforeTheFirstSettle_AndAnOpeningEdge_LandOnTheirSettle()
    {
        bool beforeSettle = true;
        SceneFixtures.HookScene scene = new(start: s =>
        {
            SceneFixtures.Open(s, new Vector2(160f, 90f), new Vector2(320f, 180f));
            s.Camera.BoundsTransition = BoundsTransition.AtSpeed(120f);
            s.Camera.Bounds = NextScreen;
            beforeSettle = s.Camera.IsTransitioning;
        });
        SceneSimulation simulation = new(scene);
        Camera camera = scene.Camera;

        simulation.Step(SceneFixtures.Step(0));
        Assert.False(beforeSettle);
        Assert.False(camera.IsTransitioning);
        Assert.Equal(NextScreen, camera.VisibleRegion);

        camera.Bounds = NextScreen with { Right = float.PositiveInfinity };
        simulation.Step(SceneFixtures.Step(1));
        Assert.False(camera.IsTransitioning);
    }

    // A 320x180 camera confined to OneScreen.
    private static (SceneSimulation Simulation, Camera Camera) Pinned(BoundsTransition transition)
    {
        SceneFixtures.HookScene scene = new(start: s =>
        {
            SceneFixtures.Open(s, new Vector2(160f, 90f), new Vector2(320f, 180f));
            s.Camera.Bounds = OneScreen;
            s.Camera.BoundsTransition = transition;
        });

        return (new SceneSimulation(scene), scene.Camera);
    }
}

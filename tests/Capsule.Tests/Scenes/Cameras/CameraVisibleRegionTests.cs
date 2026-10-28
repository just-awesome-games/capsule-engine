using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// The region a camera's step settles, which the scene's screen notifiers meet.
public sealed class CameraVisibleRegionTests
{
    private static readonly Vector2 Span = new(10f, 10f);

    [Fact]
    public void AnUnconfinedCamera_SeesItsSpanAroundItsCentre()
    {
        SceneSimulation simulation = Run(SceneFixtures.Opens(new Vector2(100f, 50f), Span));

        Assert.Equal(Rect.FromEdges(95f, 45f, 105f, 55f), simulation.Scene.Camera.VisibleRegion);
    }

    [Fact]
    public void ACameraPushedPastItsBounds_StopsAtTheEdgeItReached()
    {
        SceneSimulation simulation = Run(scene =>
        {
            SceneFixtures.Open(scene, new Vector2(100f, 0f), Span);
            scene.Camera.Bounds = Rect.FromEdges(0f, -20f, 40f, 20f);
        });

        // Confined on X to the right edge and free on Y. The centre settles where the region is.
        Assert.Equal(Rect.FromEdges(30f, -5f, 40f, 5f), simulation.Scene.Camera.VisibleRegion);
        Assert.Equal(new Vector2(35f, 0f), simulation.Scene.Camera.Center);
    }

    [Fact]
    public void ABoundsNarrowerThanTheSpan_CentresTheRegionOnItRatherThanPinningAnEdge()
    {
        SceneSimulation simulation = Run(scene =>
        {
            SceneFixtures.Open(scene, new Vector2(100f, 0f), Span);
            scene.Camera.Bounds = Rect.FromEdges(0f, -20f, 4f, 20f);
        });

        Assert.Equal(Rect.FromEdges(-3f, -5f, 7f, 5f), simulation.Scene.Camera.VisibleRegion);
    }

    // With no output on the step, every fit resolves to the declared span.
    [Theory]
    [InlineData(ViewportFit.Letterbox, false, 16f)]
    [InlineData(ViewportFit.Expand, false, 16f)]
    [InlineData(ViewportFit.Letterbox, true, 16f)]
    [InlineData(ViewportFit.Expand, true, 18f)]
    public void OnlyExpand_WidensTheRegionToTheStepsOutput(ViewportFit fit, bool output, float halfWidth)
    {
        Rect region = Run(
            scene =>
            {
                SceneFixtures.Open(scene, Vector2.Zero, new Vector2(32f, 18f));
                scene.Camera.Fit = fit;
            },
            output ? new Vector2(1000f, 500f) : default).Scene.Camera.VisibleRegion;

        Assert.Equal(Rect.FromEdges(-halfWidth, -9f, halfWidth, 9f), region);
    }

    [Fact]
    public void ACameraThatHasNotLateStepped_SeesNothingYet()
    {
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, Span));
        SceneSimulation simulation = new(scene);

        Assert.True(scene.Camera.VisibleRegion.IsEmpty);

        simulation.Step(SceneFixtures.Step());

        Assert.False(scene.Camera.VisibleRegion.IsEmpty);
    }

    private static SceneSimulation Run(Action<Scene> open, Vector2 output = default)
    {
        SceneSimulation simulation = new(new SceneFixtures.HookScene(start: open));
        simulation.Step(SceneFixtures.Step(output: output));
        return simulation;
    }
}

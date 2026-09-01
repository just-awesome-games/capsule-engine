using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class SceneCameraTests
{
    [Fact]
    public void AQuadMovingWithTheCamera_HoldsOneScreenPositionAtEveryAlpha()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(40, 10));
        drifter.Add(new QuadRenderer(Vector2.One, ColorRgba.White));

        static void Track(Scene scene, in StepContext context) => scene.Camera.Center += Vector2.UnitX;

        SceneFixtures.HookScene scene = new(start: Open(new Vector2(32, 18)), step: Track);
        scene.Add(drifter);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        CameraView camera = simulation.View.Camera;
        QuadIntent quad = simulation.View.Quads[0];

        Assert.Equal(new Vector2(8, -8), ScreenOffset(camera, quad, 0f));
        Assert.Equal(new Vector2(8, -8), ScreenOffset(camera, quad, 0.25f));
        Assert.Equal(new Vector2(8, -8), ScreenOffset(camera, quad, 0.5f));
        Assert.Equal(new Vector2(8, -8), ScreenOffset(camera, quad, 0.75f));
    }

    [Fact]
    public void ATeleportedCamera_CutsRatherThanSweeps()
    {
        static void Warp(Scene scene, in StepContext context) => scene.Camera.Teleport(new Vector2(900, 900));

        SceneFixtures.HookScene scene = new(start: Open(Vector2.Zero), step: Warp);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        CameraView camera = simulation.View.Camera;

        Assert.Equal(new Vector2(900, 900), camera.Center);
        Assert.Equal(camera.Center, camera.PreviousCenter);
    }

    [Fact]
    public void AScenesFirstFrame_OpensWhereOnStartLeftTheCamera()
    {
        SceneSimulation simulation = new(new SceneFixtures.HookScene(start: Open(new Vector2(120, 64))));

        CameraView camera = simulation.View.Camera;

        Assert.Equal(new Vector2(120, 64), camera.Center);
        Assert.Equal(camera.Center, camera.PreviousCenter);
    }

    [Fact]
    public void ACameraAimedInTheLateStep_FramesWhereItsSubjectEndedThisStep()
    {
        SceneFixtures.Drifter subject = new(new Vector2(10, 0));

        static void Follow(Scene scene, in StepContext context) =>
            scene.Camera.Center = scene.FindSingle<SceneFixtures.Drifter>().Position;

        SceneFixtures.HookScene scene = new(start: Open(new Vector2(10, 0)), lateStep: Follow);
        scene.Add(subject);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new Vector2(11, 0), subject.Position);
        Assert.Equal(subject.Position, simulation.View.Camera.Center);
    }

    [Fact]
    public void AQuadTheCameraOnlySweepsOver_SurvivesCulling()
    {
        SceneFixtures.Drifter standing = new(new Vector2(50, 0));
        standing.Add(new QuadRenderer(Vector2.One, ColorRgba.White));

        static void Sweep(Scene scene, in StepContext context) => scene.Camera.Center = new Vector2(60, 0);

        SceneFixtures.HookScene scene = new(start: Open(new Vector2(40, 0), new Vector2(10, 10)), step: Sweep);
        scene.Add(standing);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new Vector2(51, 0), Assert.Single(simulation.View.Quads.ToArray()).Position);
    }

    private static Action<Scene> Open(Vector2 center) => Open(center, new Vector2(320, 180));

    private static Action<Scene> Open(Vector2 center, Vector2 viewportSize) => scene =>
    {
        scene.Camera.Center = center;
        scene.Camera.ViewportSize = viewportSize;
    };

    private static Vector2 ScreenOffset(in CameraView camera, in QuadIntent quad, float alpha) =>
        Vector2.Lerp(quad.PreviousPosition, quad.Position, alpha) -
        Vector2.Lerp(camera.PreviousCenter, camera.Center, alpha);
}

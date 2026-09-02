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

    // The ordering the camera's own late hook exists for: the subject moves in the entity pass and
    // the camera reads it after that pass, so entity order cannot decide whether it frames this
    // step's position or the last one's.
    [Fact]
    public void AnInstalledCamerasLateStep_FramesWhereItsSubjectEndedThisStep()
    {
        SceneFixtures.Drifter subject = new(new Vector2(10, 0));
        FollowCamera camera = new(subject) { ViewportSize = new Vector2(320, 180) };

        SceneFixtures.HookScene scene = new(start: Install(camera));

        // Ahead of the subject, so a camera settled inside the entity pass would frame the
        // position the subject still held when the pass reached this slot.
        scene.Add(new SceneFixtures.Drifter(new Vector2(500, 0)));
        scene.Add(subject);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new Vector2(11, 0), subject.Position);
        Assert.Equal(subject.Position, simulation.View.Camera.Center);
    }

    [Fact]
    public void AScenesOwnLateStep_RunsBeforeItsCameras()
    {
        List<string> order = [];
        RecordingCamera camera = new(order) { ViewportSize = new Vector2(320, 180) };

        void NoteScene(Scene scene, in StepContext context) => order.Add("scene");

        SceneFixtures.HookScene scene = new(start: Install(camera), lateStep: NoteScene);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["scene", "camera"], order);
    }

    [Fact]
    public void AnInstalledCamera_CutsRatherThanSweeps()
    {
        Camera arriving = new() { Center = new Vector2(-400, 250), ViewportSize = new Vector2(320, 180) };

        void Swap(Scene scene, in StepContext context) => ((SceneFixtures.HookScene)scene).Install(arriving);

        SceneFixtures.HookScene scene = new(start: Open(new Vector2(900, 900)), lateStep: Swap);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        CameraView view = simulation.View.Camera;

        Assert.Equal(new Vector2(-400, 250), view.Center);
        Assert.Equal(view.Center, view.PreviousCenter);
    }

    // The late step must read the live camera rather than one captured before the scene's own hook,
    // or a scene that installs its camera there would settle the outgoing one for a step.
    [Fact]
    public void ACameraInstalledDuringTheLateStep_SettlesInThatSameStep()
    {
        List<string> order = [];
        RecordingCamera arriving = new(order) { ViewportSize = new Vector2(320, 180) };

        void Swap(Scene scene, in StepContext context) => ((SceneFixtures.HookScene)scene).Install(arriving);

        SceneFixtures.HookScene scene = new(start: Open(Vector2.Zero), lateStep: Swap);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["camera"], order);
    }

    // A camera discovers its subject the way an entity does, so it must be attached before it is
    // started and started only once the scene's entities have been.
    [Fact]
    public void ACameraInstalledFromTheScenesStart_IsAddedThenStartedAndSeesTheScenesEntities()
    {
        List<string> order = [];
        SceneFixtures.Drifter subject = new(new Vector2(7, 3));
        LifecycleCamera camera = new(order) { ViewportSize = new Vector2(320, 180) };

        SceneFixtures.HookScene scene = new(start: Install(camera));
        scene.Add(subject);

        using SceneSimulation simulation = new(scene);

        Assert.Equal(["added", "started"], order);
        Assert.Same(scene, camera.Scene);
        Assert.Equal(new Vector2(7, 3), simulation.View.Camera.Center);
    }

    // Entities start before the scene installs its camera, so a camera installed from an entity's
    // start is the one the scene opens with — and the camera it displaces was never the scene's,
    // so it is told nothing.
    [Fact]
    public void ACameraInstalledAsTheScenesEntitiesStart_IsTheOneTheSceneOpensWith()
    {
        List<string> log = [];
        StructuralCamera displaced = new("displaced", log);
        StructuralCamera arriving = new("arriving", log) { ViewportSize = new Vector2(320, 180) };

        SceneFixtures.HookScene scene = new();
        scene.Install(displaced);
        scene.Add(new SceneFixtures.Starter(_ => scene.Install(arriving)));
        scene.Add(new SceneFixtures.Starter(_ => log.Add("last entity")));

        using SceneSimulation simulation = new(scene);

        Assert.Equal(["last entity", "arriving+", "arriving!"], log);
        Assert.Same(arriving, scene.Camera);
        Assert.Null(displaced.Scene);
    }

    // Structural hooks pair or they leak: a scene that fails to start never installed its camera,
    // so cleaning up must not release one that was never added.
    [Fact]
    public void ASceneWhoseEntityFailsToStart_ReleasesNoCameraItNeverAdded()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        scene.Install(new StructuralCamera("camera", log));
        scene.Add(new SceneFixtures.Starter(_ => throw new InvalidOperationException("entity start failed")));

        Assert.Throws<InvalidOperationException>(() => new SceneSimulation(scene));
        Assert.Empty(log);
    }

    // The handover installs whichever camera is current once the outgoing one has been told, not
    // the one the write arrived with: a hook that installs a camera of its own finds none
    // installed and only takes the handle, so trusting the stale one would leave the scene naming
    // one camera while another framed it.
    [Fact]
    public void ACameraInstalledFromTheOutgoingCamerasRemoval_IsTheOneTheSceneFrames()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);

        MeddlingCamera usurper = new("usurper", log);
        MeddlingCamera opening = new("opening", log, onRemoved: () => scene.Install(usurper));
        scene.Install(opening);

        Camera displaced = new();
        scene.Install(displaced);

        Assert.Equal(["opening+", "opening!", "opening-", "usurper+", "usurper!"], log);
        Assert.Same(usurper, scene.Camera);
        Assert.Same(scene, usurper.Scene);
        Assert.Null(displaced.Scene);
        Assert.Null(opening.Scene);
    }

    // Install re-reads the scene's camera after the arrival hook: one that installed another from
    // it has already been released, and starting it would begin time for a camera the scene let go.
    [Fact]
    public void ACameraReplacedFromItsOwnArrivalHook_IsNeverStarted()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);

        MeddlingCamera replacement = new("replacement", log);
        MeddlingCamera arriving = new("arriving", log, onAdded: () => scene.Install(replacement));
        scene.Install(arriving);

        Assert.Equal(["arriving+", "arriving-", "replacement+", "replacement!"], log);
        Assert.Same(replacement, scene.Camera);
        Assert.Null(arriving.Scene);
    }

    [Fact]
    public void ACameraFramingAnotherScene_IsRefused()
    {
        Camera shared = new() { ViewportSize = new Vector2(320, 180) };
        SceneFixtures.HookScene framed = new(start: Install(shared));
        using SceneSimulation simulation = new(framed);

        SceneFixtures.HookScene other = new();

        Assert.Throws<InvalidOperationException>(() => other.Install(shared));
        Assert.Same(framed, shared.Scene);
        Assert.NotSame(shared, other.Camera);
    }

    private static Action<Scene> Install(Camera camera) =>
        scene => ((SceneFixtures.HookScene)scene).Install(camera);

    private static Action<Scene> Open(Vector2 center) => Open(center, new Vector2(320, 180));

    private static Action<Scene> Open(Vector2 center, Vector2 viewportSize) => scene =>
    {
        scene.Camera.Center = center;
        scene.Camera.ViewportSize = viewportSize;
    };

    private static Vector2 ScreenOffset(in CameraView camera, in QuadIntent quad, float alpha) =>
        Vector2.Lerp(quad.PreviousPosition, quad.Position, alpha) -
        Vector2.Lerp(camera.PreviousCenter, camera.Center, alpha);

    private sealed class FollowCamera(Entity subject) : Camera
    {
        protected internal override void OnLateStep(in StepContext context) => Center = subject.Position;
    }

    private sealed class RecordingCamera(List<string> log) : Camera
    {
        protected internal override void OnLateStep(in StepContext context) => log.Add("camera");
    }

    private sealed class StructuralCamera(string name, List<string> log) : Camera
    {
        protected internal override void OnAddedToScene() => log.Add($"{name}+");

        protected internal override void OnStart() => log.Add($"{name}!");

        protected internal override void OnRemovedFromScene() => log.Add($"{name}-");
    }

    /// <summary>A <see cref="StructuralCamera"/> that installs a camera of its own from a hook.</summary>
    private sealed class MeddlingCamera(string name, List<string> log, Action? onAdded = null, Action? onRemoved = null)
        : Camera
    {
        protected internal override void OnAddedToScene()
        {
            log.Add($"{name}+");
            onAdded?.Invoke();
        }

        protected internal override void OnStart() => log.Add($"{name}!");

        protected internal override void OnRemovedFromScene()
        {
            log.Add($"{name}-");
            onRemoved?.Invoke();
        }
    }

    private sealed class LifecycleCamera(List<string> log) : Camera
    {
        protected internal override void OnAddedToScene() => log.Add("added");

        protected internal override void OnStart()
        {
            log.Add("started");
            Center = Scene!.FindSingle<SceneFixtures.Drifter>().Position;
        }
    }
}

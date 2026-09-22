using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;

namespace Capsule.Tests.Scenes;

public sealed class SceneCameraTests
{
    [Fact]
    public void ASpriteMovingWithTheCamera_HoldsOneScreenPositionAtEveryAlpha()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(40, 10));
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        static void Track(Scene scene, in StepContext context) => scene.Camera.Center += Vector2.UnitX;

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(new Vector2(32, 18)), step: Track);
        scene.Add(drifter);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        CameraView camera = simulation.View.Camera;
        SpriteIntent sprite = simulation.View.Sprites[0];

        Assert.Equal(new Vector2(8, -8), ScreenOffset(camera, sprite, 0f));
        Assert.Equal(new Vector2(8, -8), ScreenOffset(camera, sprite, 0.25f));
        Assert.Equal(new Vector2(8, -8), ScreenOffset(camera, sprite, 0.5f));
        Assert.Equal(new Vector2(8, -8), ScreenOffset(camera, sprite, 0.75f));
    }

    [Fact]
    public void ATeleportedCamera_CutsRatherThanSweeps()
    {
        static void Warp(Scene scene, in StepContext context) => scene.Camera.Teleport(new Vector2(900, 900));

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero), step: Warp);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        CameraView camera = simulation.View.Camera;

        Assert.Equal(new Vector2(900, 900), camera.Center);
        Assert.Equal(camera.Center, camera.PreviousCenter);
    }

    [Fact]
    public void ACameraAimedInTheLateStep_FramesWhereItsSubjectEndedThisStep()
    {
        SceneFixtures.Drifter subject = new(new Vector2(10, 0));

        static void Follow(Scene scene, in StepContext context) =>
            scene.Camera.Center = scene.FindSingle<SceneFixtures.Drifter>().Position;

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(new Vector2(10, 0)), lateStep: Follow);
        scene.Add(subject);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new Vector2(11, 0), subject.Position);
        Assert.Equal(subject.Position, simulation.View.Camera.Center);
    }

    [Fact]
    public void ASpriteTheCameraOnlySweepsOver_SurvivesCulling()
    {
        SceneFixtures.Drifter standing = new(new Vector2(50, 0));
        standing.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        static void Sweep(Scene scene, in StepContext context) => scene.Camera.Center = new Vector2(60, 0);

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(new Vector2(40, 0), new Vector2(10, 10)), step: Sweep);
        scene.Add(standing);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new Vector2(51, 0), Assert.Single(simulation.View.Sprites.ToArray()).Position);
    }

    // Bounds confine what is drawn without moving the centre, so culling has to confine the same
    // way: this sprite is on screen only because the view was pushed right off the room's left
    // edge, and the raw sweep around the centre excludes it.
    [Fact]
    public void ASpriteConfinementBringsIntoView_SurvivesCulling()
    {
        SceneFixtures.Drifter standing = new(new Vector2(14, 4));
        standing.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(new Vector2(0, 4), new Vector2(16, 8)));
        scene.Camera.Bounds = new Rect(0f, 0f, 32f, 8f);
        scene.Add(standing);

        using SceneSimulation simulation = new(scene);

        Assert.Equal(new Vector2(14, 4), Assert.Single(simulation.View.Sprites.ToArray()).Position);
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

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(new Vector2(900, 900)), lateStep: Swap);

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

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero), lateStep: Swap);

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["camera"], order);
    }

    // A camera discovers its subject the way an entity does, so it is started only once the scene's
    // entities have been.
    [Fact]
    public void ACameraInstalledFromTheScenesStart_IsStartedAndSeesTheScenesEntities()
    {
        List<string> order = [];
        SceneFixtures.Drifter subject = new(new Vector2(7, 3));
        LifecycleCamera camera = new(order) { ViewportSize = new Vector2(320, 180) };

        SceneFixtures.HookScene scene = new(start: Install(camera));
        scene.Add(subject);

        using SceneSimulation simulation = new(scene);

        Assert.Equal(["started"], order);
        Assert.Same(scene, camera.Scene);
        Assert.Equal(new Vector2(7, 3), simulation.View.Camera.Center);
    }

    // Entities start before the scene installs its camera, so a camera installed from an entity's
    // start is the one the scene opens with, and the camera it displaces never started.
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

        Assert.Equal(["last entity", "arriving!"], log);
        Assert.Same(arriving, scene.Camera);
        Assert.Null(displaced.SceneOrNull);
    }

    // A scene that fails to start never installed its camera, so that camera never began.
    [Fact]
    public void ASceneWhoseEntityFailsToStart_NeverStartsItsCamera()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        scene.Install(new StructuralCamera("camera", log));
        scene.Add(new SceneFixtures.Starter(_ => throw new InvalidOperationException("entity start failed")));

        Assert.Throws<InvalidOperationException>(() => new SceneSimulation(scene));
        Assert.Empty(log);
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

    // Framing is the camera's and resolution is the renderer's, so what the scene sets has to reach
    // the frame view unread by the step that produced it.
    [Fact]
    public void ACamerasFitAndBounds_ReachTheFrameViewWithTheCentreItSettled()
    {
        Rect room = new(0f, 0f, 1000f, 500f);
        Camera camera = new()
        {
            Center = new Vector2(-400f, 250f),
            ViewportSize = new Vector2(320f, 180f),
            Fit = ViewportFit.Expand,
            Bounds = room,
        };

        SceneFixtures.HookScene scene = new(start: Install(camera));

        using SceneSimulation simulation = new(scene);

        CameraView view = simulation.View.Camera;

        Assert.Equal(ViewportFit.Expand, view.Fit);
        Assert.Equal(room, view.Bounds);
        Assert.Equal(new Vector2(-400f, 250f), view.Center);

        // The clamp lives in resolution alone: the camera still frames where it was pointed.
        Assert.Equal(new Rect(0f, 160f, 320f, 340f), view.Resolve(1f, new Vector2(1280f, 720f)));
    }

    // The content constructor installs the document's camera, and a subclass assigning Camera in its
    // own constructor body runs after that and still wins, the same precedence D-capsule-102 already
    // gives position, scale, band and scroll factor one tier down.
    [Fact]
    public void ADocumentsCameraInstalls_AndASubclassOverridingItInItsConstructorStillWins()
    {
        SceneContent content = SceneFixtures.Content(SceneFixtures.RoomWithoutTerrain(), SceneFixtures.Registry())
            with
        { Camera = static () => new DocumentCamera() };

        Assert.IsType<DocumentCamera>(new Scene(content).Camera);
        Assert.IsType<OverridingCamera>(new OverridingScene(content).Camera);
    }

    // The document's settings land in the base constructor, as its camera does. A subclass body runs
    // later and outranks them. An authored size replaces the tile maps' extent.
    [Fact]
    public void ADocumentsSettingsReachTheScene_AndASubclassOverridingOneInItsConstructorStillWins()
    {
        SceneDocument document = new(
            [new TileMapPlacement(SceneFixtures.TerrainId, SceneFixtures.RoomGrid())],
            SceneFixtures.TerrainId + 1,
            settings: new SceneSettings
            {
                Size = new Vector2(320, 180),
                ClearColor = new ColorRgba(16, 24, 32),
                Ambient = new ColorRgba(72, 76, 104),
                Sampling = TextureSampling.Point,
            });
        SceneContent content = SceneFixtures.Content(document, SceneFixtures.Registry());

        Scene scene = new(content);

        Assert.Equal(new Vector2(320, 180), scene.Size);
        Assert.Equal(new ColorRgba(16, 24, 32), scene.ClearColor);
        Assert.Equal(new ColorRgba(72, 76, 104), scene.Ambient);
        Assert.Equal(TextureSampling.Point, scene.Sampling);
        Assert.Equal(ColorRgba.Red, new AmbientScene(content).Ambient);
    }

    private sealed class AmbientScene : Scene
    {
        internal AmbientScene(SceneContent content) : base(content) => Ambient = ColorRgba.Red;
    }

    private sealed class DocumentCamera : Camera;

    private sealed class OverridingCamera : Camera;

    private sealed class OverridingScene : Scene
    {
        // Runs after Scene(SceneContent) has installed DocumentCamera, so this assignment is the
        // outermost one and wins.
        internal OverridingScene(SceneContent content) : base(content) => Camera = new OverridingCamera();
    }

    private static Action<Scene> Install(Camera camera) =>
        scene => ((SceneFixtures.HookScene)scene).Install(camera);

    private static Vector2 ScreenOffset(in CameraView camera, in SpriteIntent sprite, float alpha) =>
        Vector2.Lerp(sprite.PreviousPosition, sprite.Position, alpha) -
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
        protected internal override void OnStart() => log.Add($"{name}!");
    }

    private sealed class LifecycleCamera(List<string> log) : Camera
    {
        protected internal override void OnStart()
        {
            log.Add("started");
            Center = Scene!.FindSingle<SceneFixtures.Drifter>().Position;
        }
    }
}

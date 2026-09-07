using Capsule.Input;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class FrameCaptureTests : IDisposable
{
    private static readonly InputAction Shoot = new("Shoot");

    // Where BoundCaptureScene asks for its capture: the registry builds it from a parameterless
    // factory, so the path the headless test wants cannot be handed to its constructor.
    private static string BoundPath = "shot.png";

    private readonly string _directory =
        Directory.CreateTempSubdirectory(nameof(FrameCaptureTests)).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void TheHostsTake_YieldsTheRequestedPathAndClearsIt()
    {
        using SceneHost host = new(ToScene<CapturingScene>(), (in SceneTransition _) => new CapturingScene("shot.png"));

        host.Step(SceneFixtures.Step(0));

        Assert.Equal("shot.png", host.Scene.FrameCaptureRequested);
        Assert.True(host.TryTakeFrameCapture(out string path));
        Assert.Equal("shot.png", path);

        Assert.Null(host.Scene.FrameCaptureRequested);
        Assert.False(host.TryTakeFrameCapture(out _));
    }

    // The host takes at most one request per drawn frame, so a scene that asks twice between two
    // frames is served the second path and never the first.
    [Fact]
    public void ASecondRequestBeforeTheTake_ReplacesTheFirst()
    {
        CapturingScene scene = new("first.png");
        using SceneHost host = new(ToScene<CapturingScene>(), (in SceneTransition _) => scene);

        host.Step(SceneFixtures.Step(0));
        scene.Request("second.png");

        Assert.True(host.TryTakeFrameCapture(out string path));
        Assert.Equal("second.png", path);
    }

    // A capture is not a transition: it lives on the scene that raised it, so a transition in the
    // same step tears that scene down with the request still pending and no file is owed.
    [Fact]
    public void ATransition_DropsTheOutgoingScenesPendingRequest()
    {
        Scene Resolve(in SceneTransition target) => target.SceneType == typeof(CapturingScene)
            ? new CapturingScene("shot.png", leaves: true)
            : new PassiveScene();

        using SceneHost host = new(ToScene<CapturingScene>(), Resolve);

        host.Step(SceneFixtures.Step(0));

        Assert.IsType<PassiveScene>(host.Scene);
        Assert.False(host.TryTakeFrameCapture(out _));
    }

    [Fact]
    public void RunHeadless_CompletesWithARequestPendingAndWritesNoFile()
    {
        BoundPath = Path.Combine(_directory, "shots", "headless.png");
        InputTape tape = new InputScript().Tap(Key.Space).Wait(3).Build();

        HeadlessRunResult result = CapsuleEngine.Configure(
                "Capture Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [SceneRegistration.Plain(typeof(BoundCaptureScene), static () => new BoundCaptureScene())]))
            .WithFixedStep(10)
            .WithBindings(static bindings => bindings.Bind(Shoot, Key.Space))
            .WithoutCrashLog()
            .WithoutLogging()
            .RunHeadless<BoundCaptureScene>(tape);

        Assert.Equal(tape.Count, result.Steps);
        Assert.False(Directory.Exists(Path.GetDirectoryName(BoundPath)!));
        Assert.False(File.Exists(BoundPath));
    }

    // The advance that spends the tape runs the final step itself, so a request that step raised is
    // only reachable after the loop the run drives.
    [Fact]
    public void RunHeadless_ClearsARequestRaisedOnTheFinalStep()
    {
        BoundPath = Path.Combine(_directory, "final.png");
        InputTape tape = new InputScript().Wait(3).Tap(Key.Space).Build();

        BoundCaptureScene? scene = null;

        HeadlessRunResult result = CapsuleEngine.Configure(
                "Capture Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [SceneRegistration.Plain(typeof(BoundCaptureScene), () => scene = new BoundCaptureScene())]))
            .WithFixedStep(10)
            .WithBindings(static bindings => bindings.Bind(Shoot, Key.Space))
            .WithoutCrashLog()
            .WithoutLogging()
            .RunHeadless<BoundCaptureScene>(tape);

        Assert.Equal(tape.Count, result.Steps);
        Assert.Null(Assert.IsType<BoundCaptureScene>(scene).FrameCaptureRequested);
        Assert.False(File.Exists(BoundPath));
    }

    private static SceneTransition ToScene<TScene>()
        where TScene : Scene
        => SceneTransition.ToScene(typeof(TScene), null);

    private sealed class CapturingScene(string path, bool leaves = false) : Scene
    {
        internal void Request(string next) => CaptureFrame(next);

        protected override void OnStep(in StepContext context)
        {
            CaptureFrame(path);

            if (leaves)
            {
                RequestScene<PassiveScene>();
            }
        }
    }

    private sealed class PassiveScene : Scene;

    private sealed class BoundCaptureScene : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (context.Input.WasPressed(Shoot))
            {
                CaptureFrame(BoundPath);
            }
        }
    }
}

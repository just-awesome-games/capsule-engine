using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class FrameCaptureTests : IDisposable
{
    private const int DrivenSteps = 4;

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
        IInputDriver driver = new InputScript().Tap(Key.Space).Wait(3).Build();

        HeadlessRunResult result = CapsuleEngine.Configure(
                "Capture Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [SceneRegistration.Plain(typeof(BoundCaptureScene), static () => new BoundCaptureScene())]))
            .WithFixedStep(10)
            .WithInput(static input => input.Bindings.Bind(Shoot, Key.Space))
            .WithoutCrashLog()
            .WithoutLogging()
            .RunHeadless<BoundCaptureScene>(driver);

        Assert.Equal(DrivenSteps, result.Steps);
        Assert.False(Directory.Exists(Path.GetDirectoryName(BoundPath)!));
        Assert.False(File.Exists(BoundPath));
    }

    // A request the run's final step raised is taken by the loop the run drives, which is what
    // leaves nothing pending on a scene no frame will ever draw.
    [Fact]
    public void RunHeadless_ClearsARequestRaisedOnTheFinalStep()
    {
        BoundPath = Path.Combine(_directory, "final.png");
        IInputDriver driver = new InputScript().Wait(3).Tap(Key.Space).Build();

        BoundCaptureScene? scene = null;

        HeadlessRunResult result = CapsuleEngine.Configure(
                "Capture Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [SceneRegistration.Plain(typeof(BoundCaptureScene), () => scene = new BoundCaptureScene())]))
            .WithFixedStep(10)
            .WithInput(static input => input.Bindings.Bind(Shoot, Key.Space))
            .WithoutCrashLog()
            .WithoutLogging()
            .RunHeadless<BoundCaptureScene>(driver);

        Assert.Equal(DrivenSteps, result.Steps);
        Assert.Null(Assert.IsType<BoundCaptureScene>(scene).FrameCaptureRequested);
        Assert.False(File.Exists(BoundPath));
    }

    // A capture stages beside its destination, so a write that cannot land leaves what is already
    // there whole and leaves no temporary behind. A directory on the destination path denies the
    // move on every platform, where an exclusive handle on a file only denies it where locks are
    // mandatory.
    [Fact]
    public void WriteCapture_ThatCannotReplaceTheDestination_LeavesItIntactAndNoTemporary()
    {
        string path = Path.Combine(_directory, "shot.png");
        Directory.CreateDirectory(path);

        string occupant = Path.Combine(path, "occupant");
        File.WriteAllText(occupant, "an earlier capture");

        FrameRenderer.WriteCapture([1, 2, 3], path);

        Assert.Equal("an earlier capture", File.ReadAllText(occupant));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    // A path the file system rejects is a warning, not an exception thrown into the frame loop. A
    // null character passes the scene's non-blank check and fails path resolution.
    [Fact]
    public void WriteCapture_ToAPathTheFileSystemRejects_WritesNothingAndDoesNotThrow()
    {
        string path = _directory + Path.DirectorySeparatorChar + "sh\0t.png";

        FrameRenderer.WriteCapture([1, 2, 3], path);

        Assert.Empty(Directory.GetFiles(_directory));
    }

    // The staging name is unique per capture, so a file already sitting on the plain staging name
    // is neither overwritten by a capture nor deleted by one.
    [Fact]
    public void WriteCapture_LeavesAFileOnThePlainStagingNameUntouched()
    {
        string path = Path.Combine(_directory, "shot.png");
        string staging = path + FrameRenderer.TemporarySuffix;
        File.WriteAllText(staging, "an unrelated file");

        FrameRenderer.WriteCapture([1, 2, 3], path);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.Equal("an unrelated file", File.ReadAllText(staging));
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

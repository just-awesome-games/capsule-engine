using System.Numerics;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.SceneHostFixtures;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Runtime;

public sealed class SceneHostTransitionTests
{
    [Fact]
    public void ASceneRequest_ReplacesAfterTheRequestingStepAndCarriesItsPayload()
    {
        List<string> log = [];

        Scene Resolve(in SceneTransition target) => target.SceneType == typeof(FirstScene)
            ? new FirstScene(log)
            : new SecondScene(log);

        using SceneHost host = new(ToScene<FirstScene>(), Resolve, new Run());

        host.Step(Step(0));

        SecondScene second = Assert.IsType<SecondScene>(host.Scene);
        Assert.Equal("handoff", second.ReceivedPayload);
        Assert.Equal(["first.start", "first.step", "second.start", "first.stop"], log);

        host.Step(Step(1));

        Assert.Equal("second.step:1", log[^1]);
    }

    // The transition a builder's RunScene(payload) hands the host, seen from the scene's side.
    [Fact]
    public void TheTransitionTheHostBootsOn_CarriesItsPayloadIntoTheFirstScene()
    {
        SecondScene first = new([]);

        using SceneHost host = new(ToScene<SecondScene>("boot"), (in SceneTransition _) => first, new Run());
        host.Step(Step(0));

        Assert.Same(first, host.Scene);
        Assert.Equal("boot", first.ReceivedPayload);
    }

    [Fact]
    public void Restart_ReconstructsTheCurrentTargetAndKeepsItsEntryPayload()
    {
        object checkpoint = new();
        int instances = 0;

        Scene Resolve(in SceneTransition target) => new RestartingScene(++instances);

        using SceneHost host = new(ToScene<RestartingScene>(checkpoint), Resolve, new Run());
        host.Step(Step(0));

        RestartingScene restarted = Assert.IsType<RestartingScene>(host.Scene);
        Assert.Equal(2, restarted.Instance);
        Assert.Same(checkpoint, restarted.ReceivedPayload);
    }

    [Fact]
    public void Restart_CanReplaceTheEntryPayloadIncludingWithNull()
    {
        int instances = 0;

        Scene Resolve(in SceneTransition target) => new PayloadReplacingScene(++instances);

        using SceneHost host = new(ToScene<PayloadReplacingScene>(new object()), Resolve, new Run());
        host.Step(Step(0));

        PayloadReplacingScene restarted = Assert.IsType<PayloadReplacingScene>(host.Scene);
        Assert.Null(restarted.ReceivedPayload);
    }

    // A restart after a named request restarts that document, not the class the host booted on.
    [Fact]
    public void Restart_ReconstructsTheTargetTheCurrentSceneWasOpenedBy()
    {
        List<SceneTransition> seen = [];

        Scene Resolve(in SceneTransition target)
        {
            seen.Add(target);
            return target.Kind == SceneTransitionKind.Scene
                ? new HookScene(step: RequestsBossRoom)
                : new RestartingScene(1);
        }

        using SceneHost host = new(ToScene<HookScene>(), Resolve, new Run());
        host.Step(Step(0));
        host.Step(Step(1));

        Assert.Equal(
            [SceneTransitionKind.Scene, SceneTransitionKind.Named, SceneTransitionKind.Named],
            seen.Select(static target => target.Kind));
        Assert.Equal("boss-room", seen[^1].DocumentName);
    }

    [Fact]
    public void ANamedRequest_IsResolvedByDocumentNameAtTheHostBoundary()
    {
        SceneTransition seen = default;
        HookScene arrival = new();

        Scene Resolve(in SceneTransition target)
        {
            seen = target;
            return target.Kind == SceneTransitionKind.Scene ? new HookScene(step: RequestsBossRoom) : arrival;
        }

        using SceneHost host = new(ToScene<HookScene>(), Resolve, new Run());
        host.Step(Step(0));

        Assert.Equal(SceneTransitionKind.Named, seen.Kind);
        Assert.Equal("boss-room", seen.DocumentName);
        Assert.Same(arrival, host.Scene);
    }

    [Fact]
    public void ASceneOpenedByATransition_DoesNotSweepIntoPlace()
    {
        Scene Resolve(in SceneTransition target) => target.Kind == SceneTransitionKind.Scene
            ? new HookScene(step: RequestsBossRoom)
            : new HookScene(Opens(new Vector2(4000, 4000)));

        using SceneHost host = new(ToScene<HookScene>(), Resolve, new Run());
        host.Step(Step(0));

        Assert.Equal(new Vector2(4000, 4000), host.View.Camera.Center);
        Assert.Equal(host.View.Camera.Center, host.View.Camera.PreviousCenter);
    }

    [Fact]
    public void Exit_StopsTheSceneAndEndsThePersistentHost()
    {
        ExitScene scene = new();

        Scene Resolve(in SceneTransition target) => scene;

        using SceneHost host = new(ToScene<ExitScene>(), Resolve, new Run());
        List<int> preparedCounts = [];
        host.PrepareAssets = assets => preparedCounts.Add(assets.Textures.Count);
        host.Step(Step(0));

        Assert.True(host.ExitRequested);
        Assert.Equal(1, scene.Stops);
        Assert.Equal([0], preparedCounts);
    }

    private sealed class RestartingScene(int instance) : Scene
    {
        internal int Instance => instance;

        internal object? ReceivedPayload { get; private set; }

        protected override void OnStart() => ReceivedPayload = EntryPayload;

        protected override void OnStep(in StepContext context)
        {
            if (instance == 1)
            {
                Run.RequestRestart();
            }
        }
    }

    private sealed class PayloadReplacingScene(int instance) : Scene
    {
        internal object? ReceivedPayload { get; private set; }

        protected override void OnStart() => ReceivedPayload = EntryPayload;

        protected override void OnStep(in StepContext context)
        {
            if (instance == 1)
            {
                Run.RequestRestart(null);
            }
        }
    }

    private sealed class ExitScene : Scene
    {
        internal int Stops { get; private set; }

        protected override void OnStep(in StepContext context) => Run.RequestExit();

        protected override void OnStop() => Stops++;
    }
}

using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

public sealed class SceneHostTests
{
    [Fact]
    public void ASceneRequest_ReplacesAfterTheRequestingStepAndCarriesItsPayload()
    {
        List<string> log = [];

        Scene Resolve(in SceneTransition target) => target.SceneType == typeof(FirstScene)
            ? new FirstScene(log)
            : new SecondScene(log);

        using SceneHost host = new(ToScene<FirstScene>(), Resolve);

        host.Step(SceneStep(0));

        SecondScene second = Assert.IsType<SecondScene>(host.Scene);
        Assert.Equal("handoff", second.ReceivedPayload);
        Assert.Equal(["first.start", "first.step", "first.stop", "second.start"], log);

        host.Step(SceneStep(1));

        Assert.Equal("second.step:1", log[^1]);
    }

    [Fact]
    public void Restart_ReconstructsTheCurrentTargetAndKeepsItsEntryPayload()
    {
        object checkpoint = new();
        int instances = 0;

        Scene Resolve(in SceneTransition target) => new RestartingScene(++instances);

        using SceneHost host = new(ToScene<RestartingScene>(checkpoint), Resolve);
        host.Step(SceneStep(0));

        RestartingScene restarted = Assert.IsType<RestartingScene>(host.Scene);
        Assert.Equal(2, restarted.Instance);
        Assert.Same(checkpoint, restarted.ReceivedPayload);
    }

    [Fact]
    public void Restart_CanReplaceTheEntryPayloadIncludingWithNull()
    {
        int instances = 0;

        Scene Resolve(in SceneTransition target) => new PayloadReplacingScene(++instances);

        using SceneHost host = new(ToScene<PayloadReplacingScene>(new object()), Resolve);
        host.Step(SceneStep(0));

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
            return target.Kind == SceneTransitionKind.Scene ? new NameRequestingScene() : new RestartingScene(1);
        }

        using SceneHost host = new(ToScene<NameRequestingScene>(), Resolve);
        host.Step(SceneStep(0));
        host.Step(SceneStep(1));

        Assert.Equal(
            [SceneTransitionKind.Scene, SceneTransitionKind.Named, SceneTransitionKind.Named],
            seen.Select(static target => target.Kind));
        Assert.Equal("boss-room", seen[^1].DocumentName);
    }

    [Fact]
    public void ANamedRequest_IsResolvedByDocumentNameAtTheHostBoundary()
    {
        SceneTransition seen = default;

        Scene Resolve(in SceneTransition target)
        {
            seen = target;
            return target.Kind == SceneTransitionKind.Scene ? new NameRequestingScene() : new PassiveScene();
        }

        using SceneHost host = new(ToScene<NameRequestingScene>(), Resolve);
        host.Step(SceneStep(0));

        Assert.Equal(SceneTransitionKind.Named, seen.Kind);
        Assert.Equal("boss-room", seen.DocumentName);
        Assert.IsType<PassiveScene>(host.Scene);
    }

    [Fact]
    public void Exit_StopsTheSceneAndEndsThePersistentHost()
    {
        ExitScene scene = new();

        Scene Resolve(in SceneTransition target) => scene;

        using SceneHost host = new(ToScene<ExitScene>(), Resolve);
        host.Step(SceneStep(0));

        Assert.True(host.ExitRequested);
        Assert.Equal(1, scene.Stops);
    }

    [Fact]
    public void TheGamesSceneDefaults_ReachEverySceneTheHostOpens()
    {
        SceneDefaults defaults = new(TextureSampling.Point);

        using SceneHost host = new(ToScene<NameRequestingScene>(), ScenesByKind, defaults);
        host.Step(SceneStep(0));

        Assert.IsType<PassiveScene>(host.Scene);
        Assert.Equal(TextureSampling.Point, host.View.Sampling);
    }

    [Fact]
    public void ASceneOpenedByATransition_DoesNotSweepIntoPlace()
    {
        Scene Resolve(in SceneTransition target) => target.Kind == SceneTransitionKind.Scene
            ? new NameRequestingScene()
            : new FarAwayScene();

        using SceneHost host = new(ToScene<NameRequestingScene>(), Resolve);
        host.Step(SceneStep(0));

        Assert.Equal(new Vector2(4000, 4000), host.View.Camera.Center);
        Assert.Equal(host.View.Camera.Center, host.View.Camera.PreviousCenter);
    }

    // One instance, seeded once at boot, handed to every scene the host opens.
    [Fact]
    public void OneSeededSourceServesEveryScene_AcrossATransition()
    {
        List<string> log = [];
        RandomSource run = new(0xC0FFEE);

        Scene Resolve(in SceneTransition target) => target.SceneType == typeof(FirstScene)
            ? new FirstScene(log)
            : new SecondScene(log);

        using SceneHost host = new(ToScene<FirstScene>(), Resolve, default, run);

        Assert.Same(run, host.Scene.Random);

        float before = host.Scene.Random.NextFloat();
        host.Step(SceneStep(0));

        Assert.IsType<SecondScene>(host.Scene);
        Assert.Same(run, host.Scene.Random);

        // The second scene continues the sequence rather than starting it again.
        RandomSource expected = new(0xC0FFEE);
        Assert.Equal(before, expected.NextFloat());
        Assert.Equal(expected.NextFloat(), host.Scene.Random.NextFloat());
    }

    [Fact]
    public void AHostGivenNoSourceSeedsTheSceneFromTheDefault()
    {
        using SceneHost host = new(ToScene<FirstScene>(), (in SceneTransition _) => new FirstScene([]));

        Assert.Equal(RandomSource.DefaultSeed, host.Scene.Random.Seed);
        Assert.Equal(0ul, host.Scene.Random.Stream);
    }

    private static SceneTransition ToScene<TScene>(object? payload = null)
        where TScene : Scene
        => SceneTransition.ToScene(typeof(TScene), payload);

    private static Scene ScenesByKind(in SceneTransition target) =>
        target.Kind == SceneTransitionKind.Scene ? new NameRequestingScene() : new PassiveScene();

    private static StepContext SceneStep(long tick) => Capsule.Tests.Scenes.SceneFixtures.Step(tick);

    private sealed class FirstScene(List<string> log) : Scene
    {
        protected override void OnStart() => log.Add("first.start");

        protected override void OnStep(in StepContext context)
        {
            log.Add("first.step");
            RequestScene<SecondScene>("handoff");
        }

        protected override void OnStop() => log.Add("first.stop");
    }

    private sealed class SecondScene(List<string> log) : Scene
    {
        internal object? ReceivedPayload { get; private set; }

        protected override void OnStart()
        {
            ReceivedPayload = EntryPayload;
            log.Add("second.start");
        }

        protected override void OnStep(in StepContext context) => log.Add($"second.step:{context.Tick}");
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
                RequestRestart();
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
                RequestRestart(null);
            }
        }
    }

    private sealed class NameRequestingScene : Scene
    {
        protected override void OnStep(in StepContext context) => RequestScene("boss-room");
    }

    private sealed class PassiveScene : Scene;

    private sealed class FarAwayScene : Scene
    {
        protected override void OnStart()
        {
            Camera.Center = new Vector2(4000, 4000);
            Camera.ViewportSize = new Vector2(320, 180);
        }
    }

    private sealed class ExitScene : Scene
    {
        internal int Stops { get; private set; }

        protected override void OnStep(in StepContext context) => RequestExit();

        protected override void OnStop() => Stops++;
    }
}

using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.SceneHostFixtures;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Runtime;

public sealed class SceneHostFailureTests
{
    [Fact]
    public void ATransitionWhoseSceneFailsToStart_LeavesTheHostOnTheOutgoingSceneAndRethrows()
    {
        List<string> log = [];

        void RequestsOnce(Scene scene, in StepContext context)
        {
            log.Add($"first.step:{context.Tick}");
            if (context.Tick == 0)
            {
                scene.Run.RequestScene<StartFailingScene>();
            }
        }

        HookScene first = new(step: RequestsOnce);

        Scene Resolve(in SceneTransition target) => target.SceneType == typeof(StartFailingScene)
            ? new StartFailingScene(log)
            : first;

        using SceneHost host = new(ToScene<HookScene>(), Resolve, new Run());

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => host.Step(Step(0)));

        Assert.Equal("no payload", failure.Message);
        Assert.Same(first, host.Scene);
        Assert.Equal(["first.step:0", "failing.start", "failing.stop"], log);

        host.Step(Step(1));

        Assert.Equal(["first.step:0", "failing.start", "failing.stop", "first.step:1"], log);
    }

    [Fact]
    public void AnOutgoingSceneWhoseStopFails_ReleasesTheStartedIncomingSceneAndRethrows()
    {
        List<string> log = [];
        StopFailingScene? outgoing = null;

        Scene Resolve(in SceneTransition target) => target.SceneType == typeof(StopFailingScene) && outgoing is null
            ? outgoing = new StopFailingScene(log, fail: true, requestOnTickZero: true)
            : new StopFailingScene(log, fail: false, requestOnTickZero: false);

        using SceneHost host = new(ToScene<StopFailingScene>(), Resolve, new Run());

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => host.Step(Step(0)));

        Assert.Equal("stop failed", failure.Message);
        Assert.Equal(["stop:fail", "stop:ok"], log);
    }

    [Fact]
    public void AnOutgoingStopAndAnIncomingReleaseThatBothFail_SurfaceTogether()
    {
        List<string> log = [];
        bool first = true;

        Scene Resolve(in SceneTransition target)
        {
            bool requesting = first;
            first = false;

            return new StopFailingScene(log, fail: true, requestOnTickZero: requesting);
        }

        using SceneHost host = new(ToScene<StopFailingScene>(), Resolve, new Run());

        AggregateException failure = Assert.Throws<AggregateException>(() => host.Step(Step(0)));

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal(["stop:fail", "stop:fail"], log);
    }

    private sealed class StopFailingScene(List<string> log, bool fail, bool requestOnTickZero) : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (requestOnTickZero && context.Tick == 0)
            {
                Run.RequestScene<StopFailingScene>();
            }
        }

        protected override void OnStop()
        {
            log.Add(fail ? "stop:fail" : "stop:ok");
            if (fail)
            {
                throw new InvalidOperationException("stop failed");
            }
        }
    }

    private sealed class StartFailingScene(List<string> log) : Scene
    {
        protected override void OnStart()
        {
            log.Add("failing.start");
            throw new InvalidOperationException("no payload");
        }

        protected override void OnStop() => log.Add("failing.stop");
    }
}

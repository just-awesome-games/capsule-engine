using Capsule.Audio;
using Capsule.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.SceneHostFixtures;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Runtime;

public sealed class SceneHostRunStateTests
{
    /// <summary>A service the run owns and hands to every scene.</summary>
    public enum Service
    {
        Sampling,
        Random,
        Audio,
    }

    // One instance, set up once at boot, serves every scene the host opens.
    [Theory]
    [InlineData(Service.Sampling)]
    [InlineData(Service.Random)]
    [InlineData(Service.Audio)]
    public void EverySceneTheHostOpens_IsServedTheRunsOwn_AcrossATransition(Service service)
    {
        using SceneHost host = Transitioning(new Run(new RandomSource(0xC0FFEE)) { Sampling = TextureSampling.Point });
        Assert.Equal(Owned(host.Run, service), Served(host, service));

        host.Step(Step(0));

        Assert.IsType<SecondScene>(host.Scene);
        Assert.Equal(Owned(host.Run, service), Served(host, service));
    }

    // The incoming scene continues the run's sequence rather than starting it again.
    [Fact]
    public void TheSceneAfterATransition_ContinuesTheRunsRandomSequence()
    {
        using SceneHost host = Transitioning(new Run(new RandomSource(0xC0FFEE)));
        RandomSource expected = new(0xC0FFEE);
        Assert.Equal(expected.NextFloat(), host.Scene.Run.Random.NextFloat());

        host.Step(Step(0));

        Assert.IsType<SecondScene>(host.Scene);
        Assert.Equal(expected.NextFloat(), host.Scene.Run.Random.NextFloat());
    }

    // A voice a scene started and did not stop keeps playing through the transition.
    [Fact]
    public void AVoiceTheOutgoingSceneLeftPlaying_PlaysOnThroughTheTransition()
    {
        using SceneHost host = Transitioning(new Run());
        Voice ambient = host.Run.Audio.Play(new AudioPlayback(new AudioClip("hum", ".ogg", 4.0)) { Loop = true });

        host.Step(Step(0));

        Assert.IsType<SecondScene>(host.Scene);
        Assert.True(host.Run.Audio.IsPlaying(ambient));
    }

    // The capture belongs to the run. Replacing a scene does not discard what the outgoing
    // scene requested for the incoming scene's next drawn frame.
    [Fact]
    public void ATransition_PreservesTheRunsPendingFrameCapture()
    {
        static void CapturesThenLeaves(Scene scene, in StepContext context)
        {
            scene.Run.CaptureFrame("shot.png");
            scene.Run.RequestScene(new SceneKey("target-room"));
        }

        HookScene arrival = new();

        Scene Resolve(in SceneTransition target) => target.Kind == SceneTransitionKind.Scene
            ? new HookScene(step: CapturesThenLeaves)
            : arrival;

        using SceneHost host = new(ToScene<HookScene>(), Resolve, new Run());

        host.Step(Step(0));

        Assert.Same(arrival, host.Scene);
        Assert.True(host.TryTakeFrameCapture(out string path));
        Assert.Equal("shot.png", path);
    }

    private static object Owned(Run run, Service service) => service switch
    {
        Service.Sampling => run.Sampling,
        Service.Random => run.Random,
        _ => run.Audio,
    };

    // The service as the open scene sees it, or as the host draws it.
    private static object Served(SceneHost host, Service service) => service switch
    {
        Service.Sampling => host.View.Sampling,
        Service.Random => host.Scene.Run.Random,
        _ => host.Scene.Run.Audio,
    };

    // A host on a first scene that hands over to the second on its first step.
    private static SceneHost Transitioning(Run run)
    {
        static Scene Resolve(in SceneTransition target) => target.SceneType == typeof(FirstScene)
            ? new FirstScene([])
            : new SecondScene([]);

        return new SceneHost(ToScene<FirstScene>(), Resolve, run);
    }
}

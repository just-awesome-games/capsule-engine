using Capsule.Audio;
using Capsule.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.SceneHostFixtures;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Runtime;

public sealed class SceneHostRunStateTests
{
    [Fact]
    public void TheRunsSampling_ReachesEverySceneTheHostOpens()
    {
        Run run = new() { Sampling = TextureSampling.Point };
        HookScene arrival = new();

        Scene Resolve(in SceneTransition target) => target.Kind == SceneTransitionKind.Scene
            ? new HookScene(step: RequestsBossRoom)
            : arrival;

        using SceneHost host = new(ToScene<HookScene>(), Resolve, run);
        host.Step(Step(0));

        Assert.Same(arrival, host.Scene);
        Assert.Equal(TextureSampling.Point, host.View.Sampling);
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

        using SceneHost host = new(ToScene<FirstScene>(), Resolve, new Run(run));

        Assert.Same(run, host.Scene.Run.Random);

        float before = host.Scene.Run.Random.NextFloat();
        host.Step(Step(0));

        Assert.IsType<SecondScene>(host.Scene);
        Assert.Same(run, host.Scene.Run.Random);

        // The second scene continues the sequence rather than starting it again.
        RandomSource expected = new(0xC0FFEE);
        Assert.Equal(before, expected.NextFloat());
        Assert.Equal(expected.NextFloat(), host.Scene.Run.Random.NextFloat());
    }

    // One mixer, built at boot and handed to every scene the host opens, so a voice a scene started
    // and did not stop keeps playing through the transition.
    [Fact]
    public void OneMixerServesEveryScene_AcrossATransition()
    {
        Scene Resolve(in SceneTransition target) => target.SceneType == typeof(FirstScene)
            ? new FirstScene([])
            : new SecondScene([]);

        using SceneHost host = new(ToScene<FirstScene>(), Resolve, new Run());

        AudioMixer mixer = host.Run.Audio;
        Assert.Same(mixer, host.Scene.Run.Audio);

        Voice ambient = mixer.Play(new AudioPlayback(new AudioClip("hum", ".ogg", 4.0)) { Loop = true });
        host.Step(Step(0));

        Assert.IsType<SecondScene>(host.Scene);
        Assert.Same(mixer, host.Scene.Run.Audio);
        Assert.True(mixer.IsPlaying(ambient));
    }

    // The capture belongs to the run, so replacing a scene does not discard what the outgoing
    // scene requested for the incoming scene's next drawn frame.
    [Fact]
    public void ATransition_PreservesTheRunsPendingFrameCapture()
    {
        static void CapturesThenLeaves(Scene scene, in StepContext context)
        {
            scene.Run.CaptureFrame("shot.png");
            scene.Run.RequestScene("boss-room");
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
}

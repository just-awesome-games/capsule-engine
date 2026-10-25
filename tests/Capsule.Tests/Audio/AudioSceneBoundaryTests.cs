using Capsule.Assets;
using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

// What a step raised before its scene boundary sounds from the stores the outgoing scene declared,
// once. The boundary that follows exchanges or releases those stores.
[Collection(LogSinkCollection.Name)]
public sealed class AudioSceneBoundaryTests : IDisposable
{
    private const double StepSeconds = 0.1;

    private static readonly AudioClip Shot = new("shot", ".wav", 0.5);

    private static readonly AudioClip Chime = new("chime", ".wav", 0.5);

    private readonly CollectingLogSink _log = new();

    public AudioSceneBoundaryTests() => Log.UseSink(_log);

    public void Dispose() => Log.UseSink(null);

    [Fact]
    public void APlayRaisedInTheStepThatExits_SoundsFromTheResidentClip()
    {
        using Fixture fixture = new();
        using SceneHost host = new(SceneTransition.ToScene(typeof(Exits), null), static (in SceneTransition _) => new Exits(), new Run());
        FixedStepScheduler scheduler = Follow(host, fixture);

        Assert.True(scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host));

        Assert.Equal([Shot], fixture.Loaded);
        MixCommand play = Assert.Single(fixture.Posted);
        Assert.Equal(MixCommandKind.Play, play.Kind);
        Assert.NotNull(play.Samples);
        Assert.Empty(_log.Entries);
    }

    // The outgoing scene's play and the gain it set on it are applied once before the incoming
    // scene's stores replace its own, and the incoming scene's start is applied once after the step.
    [Fact]
    public void ATransitionStep_DeliversTheOutgoingScenesCommandsBeforeTheBoundaryAndTheIncomingStartAfterIt()
    {
        static Scene Resolve(in SceneTransition target) =>
            target.SceneType == typeof(Outgoing) ? new Outgoing() : new Incoming();

        using Fixture fixture = new();
        using SceneHost host = new(SceneTransition.ToScene(typeof(Outgoing), null), Resolve, new Run());
        FixedStepScheduler scheduler = Follow(host, fixture);

        Assert.False(scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host));

        Assert.IsType<Incoming>(host.Scene);
        Assert.Equal([Shot, Chime], fixture.Loaded);
        Assert.Equal(
            [(MixCommandKind.Play, 0, 1f), (MixCommandKind.SetGain, 0, 0.5f), (MixCommandKind.Play, 1, 1f)],
            fixture.Posted.Select(static command => (command.Kind, command.Slot, command.Gain)));
        Assert.Empty(_log.Entries);
    }

    // The order LoadContent wires a run with a device in.
    private static FixedStepScheduler Follow(SceneHost host, Fixture fixture)
    {
        FixedStepScheduler scheduler = new(StepSeconds, 1, new ActionBindings());
        host.PrepareAssets = fixture.Store.ChangeScene;
        host.PrepareInitialAssets();
        host.FollowSteps(scheduler, fixture.Player);

        return scheduler;
    }

    private sealed class Exits : Scene
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Add(Shot);

        protected override void OnStep(in StepContext context)
        {
            Run.Audio.Play(Shot);
            Run.RequestExit();
        }
    }

    private sealed class Outgoing : Scene
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Add(Shot);

        protected override void OnStep(in StepContext context)
        {
            Run.Audio.SetVolume(Run.Audio.Play(Shot), 0.5f);
            Run.RequestScene<Incoming>();
        }
    }

    private sealed class Incoming : Scene
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Add(Chime);

        protected override void OnStart() => Run.Audio.Play(Chime);
    }
}

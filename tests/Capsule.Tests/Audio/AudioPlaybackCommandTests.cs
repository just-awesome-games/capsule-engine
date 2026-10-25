using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.DevTools;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

public sealed class AudioPlaybackCommandTests
{
    // A resident play carries the clip's samples, and the region, the start and the bus as the mixer
    // counts them: frames of the clip's own rate, and the bus's index with 0 the master.
    [Fact]
    public void Play_PostsTheVoiceWithItsSamplesRegionStartAndBusInTheMixersTerms()
    {
        using Fixture fixture = new();
        AudioBus sfx = new("sfx");

        fixture.Apply(Command(AudioCommandKind.Play, Slot(3, 1), Theme, gain: 0.25f, pitch: 1.5f, pan: -1f, loop: true, startSeconds: 0.5, bus: sfx));

        MixCommand buses = fixture.Posted[0];
        Assert.Equal(MixCommandKind.Buses, buses.Kind);
        Assert.Single(buses.Buses!);

        MixCommand play = fixture.Posted[1];
        Assert.Equal(MixCommandKind.Play, play.Kind);
        Assert.Equal((3, 1), (play.Slot, play.Generation));
        Assert.Equal((0.25f, 1.5f, -1f), (play.Gain, play.Pitch, play.Pan));
        Assert.True(play.Loop);
        Assert.Equal((long)(Theme.DurationSeconds * ResidentRate), play.Samples!.Frames);
        Assert.Equal((2L * ResidentRate, 8L * ResidentRate), (play.LoopStart, play.LoopEnd));
        Assert.Equal(ResidentRate / 2L, play.StartFrame);
        Assert.Equal(1, play.Bus);
        Assert.False(play.Held);
    }

    // Every kind other than Play is a settled value for a voice already on the table.
    [Fact]
    public void ACommandReachesTheVoiceItsGenerationNames()
    {
        using Fixture fixture = new();
        Voice voice = Slot(3, 1);

        fixture.Apply(
            Command(AudioCommandKind.Play, voice, Step),
            Command(AudioCommandKind.SetGain, voice, gain: 0.5f),
            Command(AudioCommandKind.SetPitch, voice, pitch: 2f),
            Command(AudioCommandKind.SetPan, voice, pan: 0.5f),
            Command(AudioCommandKind.Pause, voice),
            Command(AudioCommandKind.Resume, voice),
            Command(AudioCommandKind.SetGain, Slot(3, 2), gain: 0f));

        Assert.Equal(
            [
                (MixCommandKind.Play, 1f, 1f, 0f),
                (MixCommandKind.SetGain, 0.5f, 1f, 0f),
                (MixCommandKind.SetPitch, 0f, 2f, 0f),
                (MixCommandKind.SetPan, 0f, 1f, 0.5f),
                (MixCommandKind.Pause, 0f, 1f, 0f),
                (MixCommandKind.Resume, 0f, 1f, 0f),
            ],
            fixture.Posted.Select(static command => (command.Kind, command.Gain, command.Pitch, command.Pan)));
        Assert.All(fixture.Posted, static command => Assert.Equal((3, 1), (command.Slot, command.Generation)));
    }

    // The host's suspension is a layer over the game's own pause: a hold pauses what plays and a
    // release resumes it, a voice the game paused stays paused throughout, and one played during
    // the hold starts held and sounds only on release.
    [Fact]
    public void SuspendHoldsEveryPlayingVoiceAndResumeRestoresOnlyWhatTheGameHasPlaying()
    {
        using Fixture fixture = new();

        fixture.Apply(
            Command(AudioCommandKind.Play, Slot(0, 1), Step),
            Command(AudioCommandKind.Play, Slot(1, 1), Step),
            Command(AudioCommandKind.Pause, Slot(1, 1)));
        fixture.Posted.Clear();

        fixture.Player.Suspend();
        fixture.Apply(Command(AudioCommandKind.Play, Slot(2, 1), Step));
        fixture.Player.Resume();

        Assert.Equal(
            [(MixCommandKind.Pause, 0, false), (MixCommandKind.Play, 2, true), (MixCommandKind.Resume, 0, false), (MixCommandKind.Resume, 2, false)],
            fixture.Posted.Select(static command => (command.Kind, command.Slot, command.Held)));
    }

    // What the game asks during a hold is remembered, not heard: a resume lands on release, a
    // pause outlives it.
    [Fact]
    public void AGamePauseOrResumeDuringSuspensionTakesEffectOnRelease()
    {
        using Fixture fixture = new();

        fixture.Apply(
            Command(AudioCommandKind.Play, Slot(0, 1), Step),
            Command(AudioCommandKind.Pause, Slot(0, 1)),
            Command(AudioCommandKind.Play, Slot(1, 1), Step));
        fixture.Player.Suspend();
        fixture.Posted.Clear();

        fixture.Apply(
            Command(AudioCommandKind.Resume, Slot(0, 1)),
            Command(AudioCommandKind.Pause, Slot(1, 1)));

        Assert.Empty(fixture.Posted);

        fixture.Player.Resume();

        Assert.Equal([(MixCommandKind.Resume, 0)], fixture.Posted.Select(static command => (command.Kind, command.Slot)));
    }

    // A panel command is part of the tick it forces: the sound it plays or stops is that step's
    // command, handed to the player once with the step's own.
    [Fact]
    public void APanelAudioCommand_ReachesThePlayerWithItsTickExactlyOnce()
    {
        using Fixture fixture = new();
        using OverlayRig rig = new(new SceneHost(
            SceneTransition.ToScene(typeof(StartupScene), null),
            static (in SceneTransition _) => new StartupScene(),
            new Run()));
        SceneHost host = rig.Host;
        rig.Scheduler.StepCompleted = () => fixture.Player.Apply(host.Run.Audio.Commands);
        fixture.Player.Apply(host.Run.Audio.Commands);

        rig.OpenEntityPanel("Speaker");
        rig.SelectRow("Stop");
        Assert.Equal(1, rig.Scheduler.Tick);
        rig.SelectRow("Play");
        Assert.Equal(2, rig.Scheduler.Tick);
        rig.SelectRow("Pause");
        rig.SelectRow("Resume");
        Assert.Equal(4, rig.Scheduler.Tick);

        Assert.Equal(
            [MixCommandKind.Play, MixCommandKind.Stop, MixCommandKind.Play, MixCommandKind.Pause, MixCommandKind.Resume],
            fixture.Posted.Select(static command => command.Kind));
        Assert.True(fixture.Posted[0].Loop);
    }

    // An expired one-shot raises no Stop at all. The mix thread's report retires it, and returns a
    // stream it was reading to the worker's pool.
    [Fact]
    public void AVoiceTheMixerEnded_IsRetiredWithoutACommandAndItsStreamPooled()
    {
        using Fixture fixture = new();
        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Music));
        MixCommand play = Assert.Single(fixture.Posted);

        fixture.End(play);
        fixture.Apply(Command(AudioCommandKind.SetGain, Slot(0, 1), gain: 0.5f));

        Assert.Single(fixture.Posted);
        Assert.True(SpinWait.SpinUntil(() => fixture.Streamer.Pooled == 1, TimeSpan.FromSeconds(10)), "the stream did not return to the pool in time");
    }
}

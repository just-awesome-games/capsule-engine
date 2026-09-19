using Capsule.Audio;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Audio;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Audio;

public sealed class AudioPlaybackCommandTests
{
    [Fact]
    public void Play_StartsOneVoiceCarryingTheCommandsGainPitchAndLoop()
    {
        using Fixture fixture = new();

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step, gain: 0.25f, pitch: 1.5f, loop: true));

        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(Step, voice.Clip);
        Assert.Equal(0.25f, voice.Gain);
        Assert.Equal(1.5f, voice.Pitch);
        Assert.True(voice.Loop);
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
            Command(AudioCommandKind.Pause, voice),
            Command(AudioCommandKind.Resume, voice));

        FakeVoice played = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(0.5f, played.Gain);
        Assert.Equal(2f, played.Pitch);
        Assert.False(played.Paused);
        Assert.Equal(1, played.Resumes);
    }

    // The host's suspension is a layer over the game's own pause: a hold pauses what plays and a
    // release resumes it, a voice the game paused stays paused throughout, and one played during
    // the hold starts held and sounds only on release.
    [Fact]
    public void SuspendHoldsEveryPlayingVoiceAndResumeRestoresOnlyWhatTheGameHasPlaying()
    {
        using Fixture fixture = new();
        Voice playing = Slot(0, 1);
        Voice paused = Slot(1, 1);
        Voice late = Slot(2, 1);

        fixture.Apply(
            Command(AudioCommandKind.Play, playing, Step),
            Command(AudioCommandKind.Play, paused, Step),
            Command(AudioCommandKind.Pause, paused));
        FakeVoice sounding = fixture.Backend.Voices[0];
        FakeVoice gamePaused = fixture.Backend.Voices[1];

        fixture.Player.Suspend();

        Assert.True(sounding.Paused);
        Assert.True(gamePaused.Paused);

        fixture.Apply(Command(AudioCommandKind.Play, late, Step));
        FakeVoice started = fixture.Backend.Voices[2];
        Assert.True(started.Paused);

        fixture.Player.Resume();

        Assert.False(sounding.Paused);
        Assert.Equal(1, sounding.Resumes);
        Assert.True(gamePaused.Paused);
        Assert.Equal(0, gamePaused.Resumes);
        Assert.False(started.Paused);
        Assert.Equal(1, started.Resumes);
    }

    // What the game asks during a hold is remembered, not heard: a resume lands on release, a
    // pause outlives it.
    [Fact]
    public void AGamePauseOrResumeDuringSuspensionTakesEffectOnRelease()
    {
        using Fixture fixture = new();
        Voice resumed = Slot(0, 1);
        Voice paused = Slot(1, 1);

        fixture.Apply(
            Command(AudioCommandKind.Play, resumed, Step),
            Command(AudioCommandKind.Pause, resumed),
            Command(AudioCommandKind.Play, paused, Step));
        FakeVoice toResume = fixture.Backend.Voices[0];
        FakeVoice toPause = fixture.Backend.Voices[1];

        fixture.Player.Suspend();
        fixture.Apply(
            Command(AudioCommandKind.Resume, resumed),
            Command(AudioCommandKind.Pause, paused));

        Assert.True(toResume.Paused);
        Assert.True(toPause.Paused);

        fixture.Player.Resume();

        Assert.False(toResume.Paused);
        Assert.True(toPause.Paused);
    }

    // A panel command is part of the tick it forces: the sound it plays or stops is that step's
    // command, handed to the player once with the step's own, so a Stop from the panel retires the
    // looping device voice and a Play starts exactly one.
    [Fact]
    public void APanelAudioCommand_ReachesThePlayerWithItsTickExactlyOnce()
    {
        using Fixture fixture = new();
        using SceneHost host = new(
            SceneTransition.ToScene(typeof(StartupScene), null),
            static (in SceneTransition _) => new StartupScene(),
            new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        scheduler.StepCompleted = () => fixture.Player.Apply(host.Run.Audio.Commands);
        fixture.Player.Apply(host.Run.Audio.Commands);
        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        Assert.True(voice.Loop);
        Assert.Equal(1, voice.Plays);

        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Speaker", overlay.Title);

        Choose(overlay, scheduler, host, "  Stop");
        Assert.Equal(1, scheduler.Tick);
        Assert.True(voice.Disposed);
        Assert.Single(fixture.Backend.Voices);

        Choose(overlay, scheduler, host, "  Play");
        Assert.Equal(2, scheduler.Tick);
        Assert.Same(voice, Assert.Single(fixture.Backend.Voices));
        Assert.False(voice.Disposed);
        Assert.Equal(2, voice.Plays);

        Choose(overlay, scheduler, host, "  Pause");
        Assert.True(voice.Paused);

        Choose(overlay, scheduler, host, "  Resume");
        Assert.False(voice.Paused);
        Assert.Equal(1, voice.Resumes);
        Assert.Equal(4, scheduler.Tick);
    }

    // Moves the focus down to the row reading `label` and activates it.
    private static void Choose(OverlayHost overlay, FixedStepScheduler scheduler, SceneHost host, string label)
    {
        int guard = overlay.Rows.Count;
        while (overlay.Rows[overlay.Focus].Label != label)
        {
            Assert.True(guard-- > 0, $"No row reads '{label}'.");
            Press(overlay, scheduler, host, Key.Down);
        }

        Press(overlay, scheduler, host, Key.Enter);
    }

    // A handle whose generation has moved on addresses a voice that was stolen, stopped or expired.
    [Fact]
    public void ACommandForAGenerationTheTableNoLongerHoldsIsDropped()
    {
        using Fixture fixture = new();

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step, gain: 1f));
        fixture.Apply(Command(AudioCommandKind.SetGain, Slot(0, 2), gain: 0f));

        Assert.Equal(1f, Assert.Single(fixture.Backend.Voices).Gain);
    }

    // The mixer raises the stolen voice's Stop ahead of the new voice's Play, both on one slot. The
    // sound pools its voice, so the one the steal ended is the one the new play sounds on.
    [Fact]
    public void AStolenSlotEndsItsOldVoiceBeforeTheNewOneStarts()
    {
        using Fixture fixture = new();

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step, gain: 0.25f));
        fixture.Apply(
            Command(AudioCommandKind.Stop, Slot(0, 1)),
            Command(AudioCommandKind.Play, Slot(0, 2), Step, gain: 1f));

        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(2, voice.Plays);
        Assert.False(voice.Disposed);
        Assert.Equal(1f, voice.Gain);
    }

    [Fact]
    public void Play_CarriesTheCommandsPan_AndSetPanMovesTheVoiceAfterwards()
    {
        using Fixture fixture = new();
        Voice voice = Slot(0, 1);

        fixture.Apply(Command(AudioCommandKind.Play, voice, Step, pan: -1f));

        FakeVoice played = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(-1f, played.Pan);

        fixture.Apply(Command(AudioCommandKind.SetPan, voice, pan: 0.5f));

        Assert.Equal(0.5f, played.Pan);
    }

    // An expired one-shot raises no Stop at all, so the table retires it on what the device says.
    [Fact]
    public void AVoiceTheDeviceFinishedIsRetiredWithoutACommand()
    {
        using Fixture fixture = new();
        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step));

        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        voice.Finished = true;
        fixture.Player.Update();

        Assert.True(voice.Disposed);
    }
}

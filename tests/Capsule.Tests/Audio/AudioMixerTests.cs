using Capsule.Audio;
using Capsule.Input;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Audio;

public sealed class AudioMixerTests
{
    // 0.08 s is 4.8 steps at 60 Hz, so it ends on the fifth.
    private static readonly AudioClip Step = new("step-soft", ".wav", 0.08);

    private static readonly AudioClip Theme = new("music/theme", ".ogg", 4.0);

    private static readonly AudioBus Sfx = new("sfx");

    private static readonly AudioBus Music = new("music");

    [Fact]
    public void AOneShot_RunsForCeilingOfItsDurationInSteps()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        Advance(mixer, 4);
        Assert.True(mixer.IsPlaying(voice));

        Advance(mixer, 5);
        Assert.False(mixer.IsPlaying(voice));
    }

    [Fact]
    public void AOneShotsPitch_DividesTheStepsItRunsFor()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Step) { Pitch = 2f });

        Advance(mixer, 2);
        Assert.True(mixer.IsPlaying(voice));

        Advance(mixer, 3);
        Assert.False(mixer.IsPlaying(voice));
    }

    [Fact]
    public void ALoop_NeverExpires()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Step) { Loop = true });

        Advance(mixer, 100_000);

        Assert.True(mixer.IsPlaying(voice));
    }

    [Fact]
    public void APausedVoice_KeepsTheStepsItHadLeftAndSpendsThemAfterResuming()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        // Two of the five steps spent, then held for a hundred.
        Advance(mixer, 2);
        mixer.Pause(voice);
        Advance(mixer, 102);

        Assert.True(mixer.IsPaused(voice));
        Assert.False(mixer.IsPlaying(voice));

        mixer.Resume(voice);
        Advance(mixer, 104);
        Assert.True(mixer.IsPlaying(voice));

        Advance(mixer, 105);
        Assert.False(mixer.IsPlaying(voice));
    }

    [Fact]
    public void APausedBus_HoldsItsOwnVoicesAndOnlyRaisesForTheOnesThatChange()
    {
        AudioMixer mixer = new();
        Voice held = mixer.Play(Step, Sfx);
        Voice untouched = mixer.Play(Step, Music);

        Advance(mixer, 1);
        mixer.Pause(Sfx);

        Assert.True(mixer.IsPaused(held));
        Assert.True(mixer.IsPlaying(untouched));
        Assert.Equal([(AudioCommandKind.Pause, held)], Raised(mixer));

        // Already held: pausing the bus again changes nothing to tell the host about.
        Advance(mixer, 2);
        mixer.Pause(Sfx);
        Assert.Empty(Raised(mixer));

        mixer.Resume(Sfx);
        Assert.Equal([(AudioCommandKind.Resume, held)], Raised(mixer));
        Assert.True(mixer.IsPlaying(held));
    }

    [Fact]
    public void AVoicePausedInItsOwnRight_StaysHeldWhenItsBusResumes()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step, Sfx);

        mixer.Pause(voice);
        mixer.Pause(Sfx);
        mixer.Resume(Sfx);

        Assert.True(mixer.IsPaused(voice));

        mixer.Resume(voice);
        Assert.True(mixer.IsPlaying(voice));
    }

    [Fact]
    public void AVoicePlayedOntoAPausedBus_StartsHeld()
    {
        AudioMixer mixer = new();
        mixer.Pause(Sfx);

        Voice voice = mixer.Play(Step, Sfx);

        Assert.True(mixer.IsPaused(voice));
        Assert.Equal([(AudioCommandKind.Play, voice), (AudioCommandKind.Pause, voice)], Raised(mixer));
    }

    [Fact]
    public void ABusVolume_RaisesTheResolvedProductForItsOwnLiveVoicesOnly()
    {
        AudioMixer mixer = new();
        mixer.SetVolume(AudioBus.Master, 0.5f);

        Voice sfx = mixer.Play(new AudioPlayback(Step) { Bus = Sfx, Volume = 0.5f });
        Voice music = mixer.Play(Theme, Music);

        Advance(mixer, 1);
        mixer.SetVolume(Sfx, 0.4f);

        AudioCommand raised = Assert.Single(mixer.Commands.ToArray());
        Assert.Equal(AudioCommandKind.SetGain, raised.Kind);
        Assert.Equal(sfx, raised.Voice);
        Assert.Equal(0.5f * 0.4f * 0.5f, raised.Gain);

        // Master reaches every voice whatever bus it plays on.
        Advance(mixer, 2);
        mixer.SetVolume(AudioBus.Master, 1f);
        Assert.Equal([(AudioCommandKind.SetGain, sfx), (AudioCommandKind.SetGain, music)], Raised(mixer));
    }

    [Fact]
    public void AVoicesGain_IsMasterTimesBusTimesItsOwn()
    {
        AudioMixer mixer = new();
        mixer.SetVolume(AudioBus.Master, 0.5f);
        mixer.SetVolume(Sfx, 0.5f);

        mixer.Play(new AudioPlayback(Step) { Bus = Sfx, Volume = 0.5f });

        Assert.Equal(0.125f, mixer.Commands[0].Gain);
        Assert.Equal(AudioCommandKind.Play, mixer.Commands[0].Kind);
    }

    [Fact]
    public void SetPitch_DerivesTheStepsLeftFromTheClipTimeLeft()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        // One of five steps spent, so a little under four are left; halving the rate makes eight.
        Advance(mixer, 1);
        mixer.SetPitch(voice, 0.5f);

        Advance(mixer, 8);
        Assert.True(mixer.IsPlaying(voice));

        Advance(mixer, 9);
        Assert.False(mixer.IsPlaying(voice));
    }

    // The steps a voice has left are rounded up from the clip time it has left, so the rounding is
    // what must never be rescaled: doing that hands back more time than was taken.
    [Fact]
    public void SetPitch_ThereAndBack_ManufacturesNoPlaybackTime()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        mixer.SetPitch(voice, 2f);
        mixer.SetPitch(voice, 1f);

        // Still the five steps the clip runs for on its own; no time has passed.
        Advance(mixer, 4);
        Assert.True(mixer.IsPlaying(voice));

        Advance(mixer, 5);
        Assert.False(mixer.IsPlaying(voice));
    }

    [Fact]
    public void WithNoSlotFree_TheOldestOneShotIsStolenAndALoopNeverIs()
    {
        AudioMixer mixer = new();
        Voice loop = mixer.Play(new AudioPlayback(Theme) { Loop = true });

        Voice[] live = new Voice[AudioMixer.MaxVoices - 1];
        for (int i = 0; i < live.Length; i++)
        {
            Advance(mixer, i + 1);
            live[i] = mixer.Play(Theme);
        }

        Advance(mixer, AudioMixer.MaxVoices);
        Voice latest = mixer.Play(Theme);

        Assert.False(latest.IsNone);
        Assert.True(mixer.IsPlaying(loop));
        Assert.False(mixer.IsPlaying(live[0]));
        Assert.Equal(
            [(AudioCommandKind.Stop, live[0]), (AudioCommandKind.Play, latest)],
            Raised(mixer));
    }

    [Fact]
    public void WhereEveryLiveVoiceLoops_NothingIsStolenAndNothingIsRaised()
    {
        AudioMixer mixer = new();
        for (int i = 0; i < AudioMixer.MaxVoices; i++)
        {
            mixer.Play(new AudioPlayback(Theme) { Loop = true });
        }

        Advance(mixer, 1);
        Voice refused = mixer.Play(Theme);

        Assert.True(refused.IsNone);
        Assert.Empty(Raised(mixer));
    }

    [Fact]
    public void AnExpiredOneShot_LeavesItsSlotToBeReused()
    {
        AudioMixer mixer = new();
        for (int i = 0; i < AudioMixer.MaxVoices; i++)
        {
            mixer.Play(Step);
        }

        Advance(mixer, 5);
        Voice next = mixer.Play(Step);

        Assert.True(mixer.IsPlaying(next));

        // Reclaimed, not stolen: the host was never told to stop a voice it had already finished.
        Assert.Equal([(AudioCommandKind.Play, next)], Raised(mixer));
    }

    // An expired slot stays occupied until something reaches it, so a bus-wide change must retire it
    // rather than hold it: a held voice is not expired, which would make its stale handle resolve.
    [Fact]
    public void AnExpiredOneShot_IsNotRevivedByAChangeToItsBus()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        Advance(mixer, 5);
        Assert.False(mixer.IsPlaying(voice));

        mixer.Pause(AudioBus.Master);
        mixer.Resume(AudioBus.Master);
        mixer.SetVolume(AudioBus.Master, 0.5f);

        Assert.False(mixer.IsPlaying(voice));
        Assert.False(mixer.IsPaused(voice));
        Assert.Empty(Raised(mixer));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHandleToAVoiceThatHasEnded_ReadsAsNothingAndMutatesNothing(bool stopped)
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        if (stopped)
        {
            mixer.Stop(voice);
        }
        else
        {
            Advance(mixer, 5);
        }

        // The slot is handed to another voice, so the stale handle now names a live slot.
        Advance(mixer, 6);
        Voice reused = mixer.Play(Step);
        Assert.NotEqual(voice, reused);

        mixer.Stop(voice);
        mixer.Pause(voice);
        mixer.Resume(voice);
        mixer.SetVolume(voice, 0f);
        mixer.SetPitch(voice, 4f);

        Assert.False(mixer.IsPlaying(voice));
        Assert.False(mixer.IsPaused(voice));
        Assert.True(mixer.IsPlaying(reused));
        Assert.Equal([(AudioCommandKind.Play, reused)], Raised(mixer));
    }

    [Fact]
    public void VoiceNone_ReadsAsNothingAndMutatesNothing()
    {
        AudioMixer mixer = new();

        mixer.Stop(Voice.None);
        mixer.Pause(Voice.None);
        mixer.Resume(Voice.None);
        mixer.SetVolume(Voice.None, 1f);
        mixer.SetPitch(Voice.None, 1f);

        Assert.False(mixer.IsPlaying(Voice.None));
        Assert.False(mixer.IsPaused(Voice.None));
        Assert.Empty(mixer.Commands.ToArray());
    }

    // The predicate a caller asks before starting a second voice: a voice its bus is holding still
    // owns its slot, where IsPlaying alone would say the slot were free and stack a duplicate.
    [Fact]
    public void ALiveVoice_IsLiveWhetherItIsSoundingOrHeld()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Bus = Sfx, Loop = true });

        Assert.True(mixer.IsLive(voice));

        mixer.Pause(voice);
        Assert.True(mixer.IsLive(voice));
        Assert.False(mixer.IsPlaying(voice));

        mixer.Resume(voice);
        mixer.Pause(Sfx);
        Assert.True(mixer.IsLive(voice));
        Assert.False(mixer.IsPlaying(voice));
    }

    [Fact]
    public void AStoppedVoice_IsNotLive()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        mixer.Stop(voice);

        Assert.False(mixer.IsLive(voice));
    }

    [Fact]
    public void AnExpiredOneShot_IsNotLive()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        Advance(mixer, 5);

        Assert.False(mixer.IsLive(voice));
    }

    [Fact]
    public void AStolenVoice_IsNotLive()
    {
        AudioMixer mixer = new();
        Voice oldest = mixer.Play(Theme);
        for (int i = 1; i < AudioMixer.MaxVoices; i++)
        {
            Advance(mixer, i);
            mixer.Play(Theme);
        }

        Advance(mixer, AudioMixer.MaxVoices);
        mixer.Play(Theme);

        Assert.False(mixer.IsLive(oldest));
    }

    [Fact]
    public void VoiceNoneAndAStaleHandle_AreNotLive()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);
        mixer.Stop(voice);

        // The slot is handed to another voice, so the stale handle names a live slot at an older
        // generation rather than an empty one.
        Voice reused = mixer.Play(Step);

        Assert.NotEqual(voice, reused);
        Assert.True(mixer.IsLive(reused));
        Assert.False(mixer.IsLive(voice));
        Assert.False(mixer.IsLive(Voice.None));
    }

    [Fact]
    public void APlayCommand_CarriesTheBusItWasPlayedOnAndOtherKindsCarryNone()
    {
        AudioMixer mixer = new();
        Voice sfx = mixer.Play(new AudioPlayback(Theme) { Bus = Sfx, Loop = true });
        mixer.Play(new AudioPlayback(Theme) { Loop = true });

        Assert.Equal(Sfx, mixer.Commands[0].Bus);
        Assert.Equal(AudioBus.Master, mixer.Commands[1].Bus);

        Advance(mixer, 1);
        mixer.SetVolume(sfx, 0.5f);

        Assert.Equal(AudioBus.Master, Assert.Single(mixer.Commands.ToArray()).Bus);
    }

    // Levelling a bus after playing into it within one start re-gains the voice rather than losing
    // it, so the two are only an ordering.
    [Fact]
    public void ABusVolumeSetAfterAPlayInTheSameStep_ReLevelsThatVoice()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Bus = Music, Volume = 0.5f, Loop = true });
        mixer.SetVolume(Music, 0.4f);

        Assert.Equal(
            [(AudioCommandKind.Play, voice), (AudioCommandKind.SetGain, voice)],
            Raised(mixer));
        Assert.Equal(0.5f, mixer.Commands[0].Gain);
        Assert.Equal(0.5f * 0.4f, mixer.Commands[1].Gain);
    }

    // Bus volumes are the run's: what a boot scene levels stands for every scene after it.
    [Fact]
    public void ABusVolumeOneSceneSets_LevelsAVoiceTheNextScenePlays()
    {
        static Scene Resolve(in SceneTransition target) =>
            target.SceneType == typeof(LevellingScene) ? new LevellingScene() : new MusicScene();

        using SceneHost host = new(SceneTransition.ToScene(typeof(LevellingScene), null), Resolve);

        host.Step(SceneFixtures.Step(0));

        AudioCommand played = Assert.Single(host.Audio.Commands.ToArray());
        Assert.Equal(AudioCommandKind.Play, played.Kind);
        Assert.Equal(Music, played.Bus);
        Assert.Equal(0.25f, played.Gain);
    }

    [Fact]
    public void BeginStep_DropsTheCommandsTheStepBeforeItRaised()
    {
        AudioMixer mixer = new();
        mixer.Play(Step);

        Assert.NotEmpty(mixer.Commands.ToArray());

        Advance(mixer, 1);

        Assert.Empty(mixer.Commands.ToArray());
    }

    [Fact]
    public void PlayedBeforeAnyStep_AVoiceExpiresAgainstTickZeroAndTheDefaultStepLength()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        Assert.True(mixer.IsPlaying(voice));

        Advance(mixer, 5);

        Assert.False(mixer.IsPlaying(voice));
    }

    // 0.08 s is 9.6 steps at 120 Hz. The rate reaches the mixer on the first step, after the voice
    // has already been armed against the default one.
    [Fact]
    public void PlayedBeforeAnyStep_AVoiceExpiresAgainstTheRateTheFirstStepEstablishes()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        for (long tick = 0; tick < 10; tick++)
        {
            Advance(mixer, tick, 120);
            Assert.True(mixer.IsPlaying(voice));
        }

        Advance(mixer, 10, 120);

        Assert.False(mixer.IsPlaying(voice));
    }

    [Fact]
    public void GetTime_AdvancesAtTheVoicesPitchFromTheStartItWasPlayedAt()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Pitch = 2f, StartSeconds = 0.5 });

        Assert.Equal(0.5, mixer.GetTime(voice));

        // Six steps at 60 Hz is a tenth of a second of run time, which is two tenths of clip time.
        Advance(mixer, 6);

        Assert.Equal(0.7, mixer.GetTime(voice), 6);
    }

    [Fact]
    public void GetTime_HoldsWhileTheVoicesBusIsPausedAndRunsOnAfterIt()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Bus = Music, Loop = true });

        Advance(mixer, 6);
        mixer.Pause(Music);
        Advance(mixer, 600);

        Assert.Equal(0.1, mixer.GetTime(voice), 6);

        mixer.Resume(Music);
        Advance(mixer, 606);

        Assert.Equal(0.2, mixer.GetTime(voice), 6);
    }

    // The reader plays the clip's head once and then the region, so the position past the region's end
    // reads back inside it rather than running on to the clip's duration.
    [Fact]
    public void GetTime_OfALoop_WrapsInsideItsRegionAndAtTheClipsEndWithoutOne()
    {
        AudioMixer mixer = new();
        AudioClip scored = new("music/scored", ".ogg", 8.0, new AudioLoopRegion(2.0, 6.0));

        Voice region = mixer.Play(new AudioPlayback(scored) { Loop = true });
        Voice whole = mixer.Play(new AudioPlayback(Theme) { Loop = true });

        // Seven seconds in: one second past the region's end, and three seconds past the four-second
        // clip that has no region.
        Advance(mixer, 420);

        Assert.Equal(3.0, mixer.GetTime(region), 6);
        Assert.Equal(3.0, mixer.GetTime(whole), 6);
    }

    // The host's reader repeats the region from wherever it is handed, so a start past the region's
    // end would sound from inside the region while every position read for the voice stayed an
    // interval ahead of it. The fold is the mixer's, once, so the command and the position agree.
    [Fact]
    public void ALoopStartedPastItsRegionsEnd_IsFoldedIntoTheRegion()
    {
        AudioMixer mixer = new();
        AudioClip scored = new("music/scored", ".ogg", 8.0, new AudioLoopRegion(2.0, 6.0));

        Voice voice = mixer.Play(new AudioPlayback(scored) { Loop = true, StartSeconds = 7.0 });

        Assert.Equal(3.0, mixer.Commands[0].StartSeconds, 6);
        Assert.Equal(3.0, mixer.GetTime(voice), 6);
    }

    // A one-shot ignores the region, so its start is its own whatever the region says.
    [Fact]
    public void AOneShotStartedPastTheRegionsEnd_KeepsTheStartItWasGiven()
    {
        AudioMixer mixer = new();
        AudioClip scored = new("music/scored", ".ogg", 8.0, new AudioLoopRegion(2.0, 6.0));

        Voice voice = mixer.Play(new AudioPlayback(scored) { StartSeconds = 7.0 });

        Assert.Equal(7.0, mixer.Commands[0].StartSeconds, 6);
        Assert.Equal(7.0, mixer.GetTime(voice), 6);
    }

    // A region the game sets on the clip is the same region a file-authored one is: it rides the play
    // command to the host and is what the voice's position folds into.
    [Fact]
    public void ARegionSetInCode_IsWhatThePlayCommandCarriesAndTimeFoldsInto()
    {
        AudioMixer mixer = new();
        AudioClip unscored = new("music/scored", ".ogg", 8.0, AudioLoopRegion.None);
        AudioClip scored = unscored with { LoopRegion = new AudioLoopRegion(2.0, 6.0) };

        Voice voice = mixer.Play(new AudioPlayback(scored) { Loop = true });

        Assert.Equal(new AudioLoopRegion(2.0, 6.0), mixer.Commands[0].Clip.LoopRegion);

        // Seven seconds in: one second past the region's end, so one second into the region.
        Advance(mixer, 420);

        Assert.Equal(3.0, mixer.GetTime(voice), 6);
    }

    [Fact]
    public void ARegionEndingPastTheClip_IsRefused()
    {
        AudioMixer mixer = new();
        AudioClip overrun = new("music/scored", ".ogg", 8.0, new AudioLoopRegion(2.0, 10.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.Play(new AudioPlayback(overrun) { Loop = true }));
        Assert.Empty(mixer.Commands.ToArray());
    }

    // HasRegion reads a region whose end precedes its start as no region at all, so nothing downstream
    // would report it: the mixer refuses it instead of playing the clip whole.
    [Fact]
    public void AMalformedRegion_IsRefused()
    {
        AudioMixer mixer = new();
        AudioClip inverted = new("music/scored", ".ogg", 8.0, new AudioLoopRegion(5.0, 3.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.Play(inverted));
        Assert.Empty(mixer.Commands.ToArray());
    }

    [Fact]
    public void GetTime_IsZeroForAVoiceThatIsNotLive()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        mixer.Stop(voice);

        Assert.Equal(0.0, mixer.GetTime(voice));
        Assert.Equal(0.0, mixer.GetTime(Voice.None));
    }

    // 0.08 s started at 0.05 leaves 0.03, which is 1.8 steps at 60 Hz and so ends on the second.
    [Fact]
    public void AStartOffset_ShortensTheStepsAOneShotRunsFor()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Step) { StartSeconds = 0.05 });

        Advance(mixer, 1);
        Assert.True(mixer.IsPlaying(voice));

        Advance(mixer, 2);
        Assert.False(mixer.IsPlaying(voice));
    }

    [Fact]
    public void SetPan_RaisesTheVoicesNewStereoPosition()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Pan = -1f, Loop = true });

        Assert.Equal(-1f, mixer.Commands[0].Pan);

        Advance(mixer, 1);
        mixer.SetPan(voice, 0.25f);

        AudioCommand raised = Assert.Single(mixer.Commands.ToArray());
        Assert.Equal(AudioCommandKind.SetPan, raised.Kind);
        Assert.Equal(voice, raised.Voice);
        Assert.Equal(0.25f, raised.Pan);
    }

    [Theory]
    [InlineData(-1.001f)]
    [InlineData(1.001f)]
    [InlineData(float.NaN)]
    public void APanOutsideTheStereoRange_IsRefused(float pan)
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetPan(voice, pan));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.Play(new AudioPlayback(Step) { Pan = pan }));
    }

    // Zero is the exception: it is where a clip plays from anyway, so a clip measured at no duration
    // still starts and ends on the step it started.
    [Theory]
    [InlineData(-0.001)]
    [InlineData(0.08)]
    [InlineData(1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AStartOutsideTheClip_IsRefused(double startSeconds)
    {
        AudioMixer mixer = new();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => mixer.Play(new AudioPlayback(Step) { StartSeconds = startSeconds }));
    }

    // The upper bound on a start is not a bound on zero: a clip measured at no duration is playable,
    // and ends on the step it started.
    [Fact]
    public void AClipOfNoDuration_StillPlaysFromItsStart()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioClip("silence", ".wav", 0.0));

        Assert.Equal(AudioCommandKind.Play, mixer.Commands[0].Kind);
        Assert.False(mixer.IsLive(voice));
    }

    [Theory]
    [InlineData(-0.001f)]
    [InlineData(1.001f)]
    [InlineData(float.NaN)]
    public void AVolumeOutsideTheUnitRange_IsRefused(float volume)
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetVolume(voice, volume));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetVolume(Sfx, volume));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.Play(new AudioPlayback(Step) { Volume = volume }));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void APitchThatIsNotPositiveAndFinite_IsRefused(float pitch)
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetPitch(voice, pitch));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.Play(new AudioPlayback(Step) { Pitch = pitch }));
    }

    // The constructor's defaults are what a playback carries, so the default value carries none.
    [Fact]
    public void ADefaultPlayback_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioMixer().Play(default(AudioPlayback)));

    private sealed class LevellingScene : Scene
    {
        protected override void OnStart() => Audio.SetVolume(Music, 0.25f);

        protected override void OnStep(in StepContext context) => RequestScene<MusicScene>();
    }

    private sealed class MusicScene : Scene
    {
        protected override void OnStart() => Audio.Play(new AudioPlayback(Theme) { Bus = Music, Loop = true });
    }

    private static void Advance(AudioMixer mixer, long tick) =>
        mixer.BeginStep(Capsule.Tests.Scenes.SceneFixtures.Step(tick));

    private static void Advance(AudioMixer mixer, long tick, int stepHertz) =>
        mixer.BeginStep(new StepContext(1.0 / stepHertz, new InputState(new ActionBindings()), tick));

    private static (AudioCommandKind Kind, Voice Voice)[] Raised(AudioMixer mixer)
    {
        ReadOnlySpan<AudioCommand> commands = mixer.Commands;
        (AudioCommandKind Kind, Voice Voice)[] raised = new (AudioCommandKind, Voice)[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            raised[i] = (commands[i].Kind, commands[i].Voice);
        }

        return raised;
    }
}

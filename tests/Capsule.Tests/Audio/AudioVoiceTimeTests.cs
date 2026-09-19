using Capsule.Audio;
using static Capsule.Tests.Audio.AudioMixerFixtures;

namespace Capsule.Tests.Audio;

public sealed class AudioVoiceTimeTests
{
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

        // Still the steps the clip runs for on its own; no time has passed.
        Advance(mixer, StepEnds - 1);
        Assert.True(mixer.IsPlaying(voice));

        Advance(mixer, StepEnds);
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
}

using Capsule.Audio;
using static Capsule.Tests.Audio.AudioMixerFixtures;

namespace Capsule.Tests.Audio;

public sealed class AudioVoiceLifetimeTests
{
    // How a handle came to name no live voice.
    public enum Ending
    {
        Stopped,
        Expired,
        Stolen,
        SlotReused,
        NeverPlayed,
    }

    [Fact]
    public void AOneShot_RunsForCeilingOfItsDurationInSteps()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        Advance(mixer, StepEnds - 1);
        Assert.True(mixer.IsPlaying(voice));

        Advance(mixer, StepEnds);
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

        Advance(mixer, StepEnds);
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

        Advance(mixer, StepEnds);
        Assert.False(mixer.IsPlaying(voice));

        mixer.Pause(AudioBus.Master);
        mixer.Resume(AudioBus.Master);
        mixer.SetVolume(AudioBus.Master, 0.5f);

        Assert.False(mixer.IsPlaying(voice));
        Assert.False(mixer.IsPaused(voice));
        Assert.Empty(Raised(mixer));
    }

    [Theory]
    [InlineData(Ending.Stopped)]
    [InlineData(Ending.Expired)]
    [InlineData(Ending.NeverPlayed)]
    public void AHandleToNoLiveVoice_ReadsAsNothingAndMutatesNothing(Ending ending)
    {
        AudioMixer mixer = new();
        Voice dead = Voice.None;

        if (ending != Ending.NeverPlayed)
        {
            dead = mixer.Play(Step);
            if (ending == Ending.Stopped)
            {
                mixer.Stop(dead);
            }
            else
            {
                Advance(mixer, StepEnds);
            }
        }

        // The slot is handed to another voice, so the stale handle now names a live slot.
        Advance(mixer, StepEnds + 1);
        Voice reused = mixer.Play(Step);
        Assert.NotEqual(dead, reused);

        mixer.Stop(dead);
        mixer.Pause(dead);
        mixer.Resume(dead);
        mixer.SetVolume(dead, 0f);
        mixer.SetPitch(dead, 4f);

        Assert.False(mixer.IsPlaying(dead));
        Assert.False(mixer.IsPaused(dead));
        Assert.True(mixer.IsPlaying(reused));
        Assert.Equal([(AudioCommandKind.Play, reused)], Raised(mixer));
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

    // However a voice ends, its handle is dead from then on, and so is Voice.None.
    [Theory]
    [InlineData(Ending.Stopped)]
    [InlineData(Ending.Expired)]
    [InlineData(Ending.Stolen)]
    [InlineData(Ending.SlotReused)]
    public void AVoiceThatHasEnded_IsNotLive(Ending ending)
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(ending == Ending.Stolen ? Theme : Step);

        switch (ending)
        {
            case Ending.Stopped:
                mixer.Stop(voice);
                break;

            case Ending.Expired:
                Advance(mixer, StepEnds);
                break;

            case Ending.Stolen:
                for (int i = 1; i < AudioMixer.MaxVoices; i++)
                {
                    Advance(mixer, i);
                    mixer.Play(Theme);
                }

                Advance(mixer, AudioMixer.MaxVoices);
                mixer.Play(Theme);
                break;

            default:
                // The slot is handed to another voice, so the stale handle names a live slot at an
                // older generation rather than an empty one.
                mixer.Stop(voice);
                Voice reused = mixer.Play(Step);

                Assert.NotEqual(voice, reused);
                Assert.True(mixer.IsLive(reused));
                break;
        }

        Assert.False(mixer.IsLive(voice));
        Assert.False(mixer.IsLive(Voice.None));
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
}

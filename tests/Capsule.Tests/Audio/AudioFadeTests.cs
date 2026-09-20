using Capsule.Audio;
using static Capsule.Tests.Audio.AudioMixerFixtures;

namespace Capsule.Tests.Audio;

public sealed class AudioFadeTests
{
    private static readonly long OneSecondTicks = (long)Math.Ceiling((double)StepContext.DefaultStepHertz);

    [Fact]
    public void AVoiceFade_RaisesOneSetGainPerStepAndLandsExactlyOnTarget()
    {
        AudioMixer mixer = new();
        mixer.SetVolume(AudioBus.Master, 0.5f);
        mixer.SetVolume(Sfx, 0.5f);
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Bus = Sfx, Volume = 0.2f, Loop = true });

        mixer.FadeVolume(voice, 0.8f, 0.5f);
        Assert.Equal([(AudioCommandKind.Play, voice)], Raised(mixer));

        long ticks = (long)Math.Ceiling(0.5 * StepContext.DefaultStepHertz);

        for (long tick = 1; tick < ticks; tick++)
        {
            Advance(mixer, tick);
            AudioCommand raised = Assert.Single(mixer.Commands.ToArray());
            Assert.Equal(AudioCommandKind.SetGain, raised.Kind);
            Assert.Equal(voice, raised.Voice);
        }

        Advance(mixer, ticks);
        AudioCommand landing = Assert.Single(mixer.Commands.ToArray());
        Assert.Equal(AudioCommandKind.SetGain, landing.Kind);
        Assert.Equal(0.5f * 0.5f * 0.8f, landing.Gain);
    }

    [Fact]
    public void SetVolumeMidFade_CancelsTheRamp()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Loop = true });

        mixer.FadeVolume(voice, 1f, 1f);
        mixer.SetVolume(voice, 0.3f);

        AudioCommand raised = mixer.Commands[^1];
        Assert.Equal(AudioCommandKind.SetGain, raised.Kind);
        Assert.Equal(0.3f, raised.Gain);

        Advance(mixer, 1);
        Assert.Empty(mixer.Commands.ToArray());
        Advance(mixer, OneSecondTicks);
        Assert.Empty(mixer.Commands.ToArray());
    }

    [Fact]
    public void StopWithSeconds_RaisesStopAloneOnTheLandingTick()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Loop = true });

        mixer.Stop(voice, 0.5f);
        long ticks = (long)Math.Ceiling(0.5 * StepContext.DefaultStepHertz);

        Advance(mixer, ticks - 1);
        Assert.True(mixer.IsLive(voice));

        Advance(mixer, ticks);
        Assert.Equal([(AudioCommandKind.Stop, voice)], Raised(mixer));
        Assert.False(mixer.IsLive(voice));
    }

    [Fact]
    public void ABusFadeAndAVoiceFadeOnTheSameVoice_RaiseTheProductPerStepAndLeaveOtherBusesAlone()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Bus = Music, Loop = true });
        mixer.Play(new AudioPlayback(Theme) { Bus = Sfx, Loop = true });

        mixer.FadeVolume(Music, 0.5f, 1f);
        mixer.FadeVolume(voice, 0.5f, 1f);

        Advance(mixer, 1);
        AudioCommand raised = Assert.Single(mixer.Commands.ToArray());
        Assert.Equal(voice, raised.Voice);
        Assert.Equal(AudioCommandKind.SetGain, raised.Kind);

        Advance(mixer, OneSecondTicks);
        AudioCommand landing = Assert.Single(mixer.Commands.ToArray());
        Assert.Equal(0.5f * 0.5f, landing.Gain);
    }

    [Fact]
    public void CrossFade_HoldsEqualPowerAndEndsWithTheOutgoingVoiceStopped()
    {
        AudioMixer mixer = new();
        const float target = 0.8f;
        Voice from = mixer.Play(new AudioPlayback(Theme) { Bus = Music, Volume = target, Loop = true });

        Voice to = mixer.CrossFade(
            from,
            new AudioPlayback(Theme) { Bus = Music, Volume = target, Loop = true },
            1f);

        AudioCommand played = mixer.Commands[^1];
        Assert.Equal(AudioCommandKind.Play, played.Kind);
        Assert.Equal(0f, played.Gain);

        long ticks = (long)Math.Ceiling(1.0 * StepContext.DefaultStepHertz);

        for (long tick = 1; tick <= ticks; tick++)
        {
            Advance(mixer, tick);

            float gainIn = 0f;
            float gainOut = 0f;
            foreach (AudioCommand command in mixer.Commands)
            {
                if (command.Kind != AudioCommandKind.SetGain)
                {
                    continue;
                }

                if (command.Voice == to)
                {
                    gainIn = command.Gain;
                }
                else if (command.Voice == from)
                {
                    gainOut = command.Gain;
                }
            }

            Assert.True(Math.Abs((gainIn * gainIn) + (gainOut * gainOut) - (target * target)) < 1e-4f);
        }

        Assert.False(mixer.IsLive(from));
        AudioCommand landing = mixer.Commands[^1];
        Assert.Equal(to, landing.Voice);
        Assert.Equal(target, landing.Gain);
    }

    [Fact]
    public void CrossFadeFromNone_IsAPlainFadeIn()
    {
        AudioMixer mixer = new();

        Voice to = mixer.CrossFade(
            Voice.None,
            new AudioPlayback(Theme) { Bus = Music, Volume = 0.6f, Loop = true },
            0.5f);

        Assert.Equal(0f, mixer.Commands[^1].Gain);

        long ticks = (long)Math.Ceiling(0.5 * StepContext.DefaultStepHertz);
        Advance(mixer, ticks);

        AudioCommand landing = Assert.Single(mixer.Commands.ToArray());
        Assert.Equal(AudioCommandKind.SetGain, landing.Kind);
        Assert.Equal(to, landing.Voice);
        Assert.Equal(0.6f, landing.Gain);
    }

    [Fact]
    public void AStepLengthChangeMidFade_LandsOnTheSameWallClockSeconds()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Loop = true });

        // A 1 s fade at 60 Hz is 60 ticks. Changed to 30 Hz on tick 30, it lands on tick 45.
        mixer.FadeVolume(voice, 1f, 1f);

        for (long tick = 1; tick < 30; tick++)
        {
            Advance(mixer, tick);
        }

        Advance(mixer, 30, 30);

        for (long tick = 31; tick < 45; tick++)
        {
            Advance(mixer, tick, 30);
            Assert.Equal(AudioCommandKind.SetGain, Assert.Single(mixer.Commands.ToArray()).Kind);
        }

        Advance(mixer, 45, 30);
        AudioCommand landing = Assert.Single(mixer.Commands.ToArray());
        Assert.Equal(AudioCommandKind.SetGain, landing.Kind);
        Assert.Equal(1f, landing.Gain);

        Advance(mixer, 46, 30);
        Assert.Empty(mixer.Commands.ToArray());
    }

    [Fact]
    public void AFadeOnAHeldVoice_RaisesSetGainWhileHeld()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Loop = true });
        mixer.Pause(voice);

        mixer.FadeVolume(voice, 0.2f, 0.5f);
        Advance(mixer, 1);

        AudioCommand raised = Assert.Single(mixer.Commands.ToArray());
        Assert.Equal(AudioCommandKind.SetGain, raised.Kind);
        Assert.True(mixer.IsPaused(voice));
    }
}

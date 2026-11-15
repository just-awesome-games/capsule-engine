using Capsule.Audio;
using static Capsule.Tests.Audio.AudioMixerFixtures;

namespace Capsule.Tests.Audio;

// A bus's effects in the pure mixer: the target a game reads back, the per-field ramps it steps, and the
// levels it raises for the host.
public sealed class AudioEffectsTests
{
    private static readonly AudioEcho Echo = new(0.048f) { Feedback = 0.53f, Volume = 0.78f, LowCutHertz = 1100f, HighCutHertz = 9100f };

    [Fact]
    public void GetEffects_ReadsTheTargetFromTheCallAndNoneForAnUntouchedBus()
    {
        AudioMixer mixer = new();
        Assert.Equal(default, mixer.GetEffects(Sfx));

        AudioEffects echoing = new() { Echo = Echo };
        mixer.SetEffects(Sfx, echoing);
        Assert.Equal(echoing, mixer.GetEffects(Sfx));

        Advance(mixer, 1);
        mixer.FadeEffects(Sfx, echoing with { LowPassHertz = 900f }, 0.5f);

        Assert.Equal(900f, mixer.GetEffects(Sfx).LowPassHertz);
        Assert.Empty(mixer.Commands.ToArray());
    }

    // A second fade that changes only the low-pass leaves the echo's volume on its own ramp, and the
    // cutoff moves evenly in octaves: halfway in time is the geometric mean of its ends.
    [Fact]
    public void AFade_RampsOnlyTheFieldsWhoseTargetChangedAndCutoffsInLogFrequency()
    {
        AudioMixer mixer = new();
        mixer.SetEffects(Sfx, new AudioEffects { Echo = Echo });
        mixer.FadeEffects(Sfx, new AudioEffects { Echo = Echo with { Volume = 0.2f } }, 1f);

        for (long tick = 1; tick <= 30; tick++)
        {
            Advance(mixer, tick);
        }

        mixer.FadeEffects(Sfx, mixer.GetEffects(Sfx) with { LowPassHertz = 900f }, 1f);

        for (long tick = 31; tick < 60; tick++)
        {
            Advance(mixer, tick);
        }

        Assert.True(Levels(mixer).EchoVolume > 0.2f);

        Advance(mixer, 60);
        Assert.Equal(0.2f, Levels(mixer).EchoVolume);
        Assert.Equal(MathF.Sqrt(20_000f * 900f), Levels(mixer).LowPassHertz, 0.01f * 4243f);

        for (long tick = 61; tick <= 90; tick++)
        {
            Advance(mixer, tick);
        }

        Assert.Equal(900f, Levels(mixer).LowPassHertz, 0.01f);
        Assert.Equal(0.53f, Levels(mixer).EchoFeedback);

        Advance(mixer, 91);
        Assert.Empty(mixer.Commands.ToArray());
    }

    // An echo turning on takes its delay at the call and fades its volume in from 0. Faded to none, its
    // volume and every cutoff land off, and its line goes on the landing step, not before.
    [Fact]
    public void AnEchoFadedInAndOut_TakesItsDelayAtTheCallAndDropsItOnlyOnceSilent()
    {
        AudioMixer mixer = new();
        mixer.FadeEffects(Sfx, new AudioEffects { Echo = Echo, LowPassHertz = 900f }, 0.5f);

        EffectLevels opened = Levels(mixer);
        Assert.Equal((0.048f, 0f, 0.53f), (opened.EchoDelaySeconds, opened.EchoVolume, opened.EchoFeedback));
        Assert.Equal(20_000f, opened.LowPassHertz);

        Advance(mixer, 30);
        Assert.Equal((0.78f, 900f), (Levels(mixer).EchoVolume, MathF.Round(Levels(mixer).LowPassHertz)));

        mixer.FadeEffects(Sfx, default, 0.5f);
        Advance(mixer, 59);
        Assert.Equal(0.048f, Levels(mixer).EchoDelaySeconds);
        Assert.True(Levels(mixer).EchoVolume > 0f);

        Advance(mixer, 60);
        EffectLevels closed = Levels(mixer);
        Assert.Equal((0f, 0f, 20_000f), (closed.EchoDelaySeconds, closed.EchoVolume, closed.LowPassHertz));
        Assert.Equal(default, mixer.GetEffects(Sfx));
    }

    // A game that sets its space on every scene load must not cut its own echo.
    [Fact]
    public void SettingOrFadingToTheCurrentTarget_RaisesNothing()
    {
        AudioMixer mixer = new();
        AudioEffects effects = new() { Echo = Echo, HighPassHertz = 80f };
        mixer.SetEffects(Sfx, effects);

        Advance(mixer, 1);
        mixer.SetEffects(Sfx, effects);
        mixer.FadeEffects(Sfx, effects, 0.3f);
        Assert.Empty(mixer.Commands.ToArray());

        Advance(mixer, 2);
        Assert.Empty(mixer.Commands.ToArray());
    }

    // Neither the delay nor InvertLeft ramps: a fade that changes them raises both on the call, while the rest
    // of the echo waits for the next step.
    [Fact]
    public void AChangedDelayOrInvertLeft_TakesEffectOnTheFadeCall()
    {
        AudioMixer mixer = new();
        mixer.SetEffects(Sfx, new AudioEffects { Echo = Echo });

        Advance(mixer, 1);
        mixer.FadeEffects(Sfx, new AudioEffects { Echo = Echo with { DelaySeconds = 0.1f, Feedback = 0.2f, InvertLeft = true } }, 1f);

        Assert.Equal((0.1f, true, 0.53f), (Levels(mixer).EchoDelaySeconds, Levels(mixer).EchoInvertLeft, Levels(mixer).EchoFeedback));
        Advance(mixer, 2);
        Assert.True(Levels(mixer).EchoFeedback < 0.53f);
    }

    [Fact]
    public void SetEffectsMidFade_CancelsEveryRamp()
    {
        AudioMixer mixer = new();
        mixer.FadeEffects(Sfx, new AudioEffects { HighPassHertz = 400f, Echo = Echo }, 1f);

        Advance(mixer, 1);
        mixer.SetEffects(Sfx, mixer.GetEffects(Sfx));
        Assert.Equal((400f, 0.78f), (MathF.Round(Levels(mixer).HighPassHertz), Levels(mixer).EchoVolume));

        Advance(mixer, 2);
        Assert.Empty(mixer.Commands.ToArray());

        // The echo's feedback and cuts fading when the echo is set off stop fading with it.
        mixer.FadeEffects(Sfx, new AudioEffects { Echo = Echo with { Feedback = 0.2f, LowCutHertz = 500f, HighCutHertz = 3000f } }, 1f);
        Advance(mixer, 3);
        mixer.SetEffects(Sfx, default);
        Advance(mixer, 4);
        Assert.Empty(mixer.Commands.ToArray());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public void EffectsOutsideTheirRanges_AreRefusedByBothCalls(int row, bool refused)
    {
        AudioEcho echo = new(0.1f);
        AudioEffects effects = row switch
        {
            0 => new AudioEffects { HighPassHertz = 19.9f },
            1 => new AudioEffects { LowPassHertz = 20_001f },
            2 => new AudioEffects { LowPassHertz = float.NaN },
            3 => new AudioEffects { Echo = default(AudioEcho) },
            4 => new AudioEffects { Echo = new AudioEcho(4.01f) },
            5 => new AudioEffects { Echo = echo with { Feedback = 1f } },
            6 => new AudioEffects { Echo = echo with { Feedback = -0.1f } },
            7 => new AudioEffects { Echo = echo with { Volume = 1.01f } },
            8 => new AudioEffects { Echo = echo with { LowCutHertz = 10f } },
            9 => new AudioEffects { Echo = echo with { HighCutHertz = float.PositiveInfinity } },

            // The ends of every range.
            _ => new AudioEffects
            {
                HighPassHertz = 20f,
                LowPassHertz = 20_000f,
                Echo = new AudioEcho(4f) { Feedback = 0f, Volume = 1f, LowCutHertz = 20f, HighCutHertz = 20_000f },
            },
        };
        AudioMixer mixer = new();

        if (refused)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetEffects(Sfx, effects));
            Assert.Throws<ArgumentOutOfRangeException>(() => mixer.FadeEffects(Sfx, effects, 1f));
        }
        else
        {
            mixer.SetEffects(Sfx, effects);
            mixer.FadeEffects(Sfx, default, 1f);
        }
    }

    // The levels of the last SetBusEffects raised.
    private static EffectLevels Levels(AudioMixer mixer)
    {
        AudioCommand[] commands = mixer.Commands.ToArray();

        return commands.Last(static command => command.Kind == AudioCommandKind.SetBusEffects).Effects;
    }
}

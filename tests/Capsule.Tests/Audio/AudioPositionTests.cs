using System.Numerics;
using Capsule.Animation;
using Capsule.Audio;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Audio;

public sealed class AudioPositionTests
{
    private const float ViewWidth = 200f;

    [Theory]
    [InlineData(Ease.Linear, 0f, 1f)]
    [InlineData(Ease.Linear, 25f, 0.75f)]
    [InlineData(Ease.InQuad, 50f, 0.75f)]
    [InlineData(Ease.InQuad, 100f, 0f)]
    [InlineData(Ease.InQuad, 150f, 0f)]
    public void AVoicesGain_FollowsItsFalloffAndIsSilentFromMaxDistance(Ease falloff, float distance, float gain)
    {
        AudioMixer mixer = Heard(Vector2.Zero);

        Voice voice = mixer.Play(new AudioPlayback(AudioMixerFixtures.Theme)
        {
            Loop = true,
            Position = new Vector2(0f, distance),
            Range = new AudioRange(100f) { Falloff = falloff },
        });
        mixer.Settle(Vector2.Zero, ViewWidth);

        Assert.Equal(gain, GainOf(mixer, voice), 6);
    }

    // Half a view width off centre pans halfway. The voice's own hard-left pan, set before and after
    // the play, never reaches the host.
    [Theory]
    [InlineData(100f, 0.5f)]
    [InlineData(-200f, -1f)]
    [InlineData(500f, 1f)]
    public void APositionalVoice_PansByItsOffsetInViewWidthsAndIgnoresItsOwnPan(float x, float pan)
    {
        AudioMixer mixer = Heard(Vector2.Zero);

        Voice voice = mixer.Play(new AudioPlayback(AudioMixerFixtures.Theme)
        {
            Loop = true,
            Pan = -1f,
            Position = new Vector2(x, 0f),
            Range = new AudioRange(1000f),
        });
        mixer.SetPan(voice, -1f);

        Assert.Equal(pan, PanOf(mixer, voice), 6);
    }

    // The settle runs after the camera has settled. Settling at the step's opening would hear the
    // region the step before framed and raise nothing here.
    [Fact]
    public void TheSettle_HearsTheCameraThisStepSettled()
    {
        using SimulationHost run = Simulate(new Vector2(100f, 0f), Positional(new AudioRange(200f)), out Voice voice);
        run.Step();
        Assert.Equal(0.5f, GainOf(run.Run.Audio, voice), 6);

        run.Scene.Camera.Center = new Vector2(100f, 0f);
        run.Step();

        Assert.Equal(1f, GainOf(run.Run.Audio, voice), 6);
    }

    [Fact]
    public void ASourcesVoice_FollowsItsEntityAcrossSteps()
    {
        AudioSource source = Positional(new AudioRange(160f));
        using SimulationHost run = Simulate(Vector2.Zero, source, out Voice voice);
        run.Step();

        source.Entity!.Position = new Vector2(-80f, 0f);
        run.Step();
        Assert.Equal(0.5f, GainOf(run.Run.Audio, voice), 6);
        Assert.Equal(-80f / SceneFixtures.Viewport.X, PanOf(run.Run.Audio, voice), 6);

        source.Entity.Position = new Vector2(0f, 200f);
        run.Step();
        Assert.Equal(0f, GainOf(run.Run.Audio, voice));
    }

    [Fact]
    public void AVoiceOutOfRange_KeepsItsTimeAndContinuesWhereItWasWhenHeardAgain()
    {
        AudioMixer mixer = Heard(Vector2.Zero);
        Voice voice = mixer.Play(new AudioPlayback(AudioMixerFixtures.Theme)
        {
            Loop = true,
            Position = new Vector2(500f, 0f),
            Range = new AudioRange(100f),
        });
        Assert.Equal(0f, GainOf(mixer, voice));

        for (long tick = 1; tick <= 30; tick++)
        {
            AudioMixerFixtures.Advance(mixer, tick);
            mixer.Settle(Vector2.Zero, ViewWidth);
        }

        Assert.True(mixer.IsPlaying(voice));
        Assert.Equal(30.0 / StepContext.DefaultStepHertz, mixer.GetTime(voice), 9);

        AudioMixerFixtures.Advance(mixer, 31);
        mixer.Settle(new Vector2(500f, 0f), ViewWidth);

        Assert.Equal(1f, GainOf(mixer, voice));
        Assert.Equal(31.0 / StepContext.DefaultStepHertz, mixer.GetTime(voice), 9);
    }

    [Fact]
    public void ANonFiniteAnchorOrListener_IsSilentAndCentred()
    {
        AudioMixer mixer = Heard(Vector2.Zero);
        Voice lost = mixer.Play(
            new AudioPlayback(AudioMixerFixtures.Theme) { Loop = true, Range = new AudioRange(100f) },
            new Anchor(new Vector2(float.PositiveInfinity, 0f)));
        Voice near = mixer.Play(new AudioPlayback(AudioMixerFixtures.Theme)
        {
            Loop = true,
            Position = new Vector2(50f, 0f),
            Range = new AudioRange(100f),
        });

        mixer.Settle(Vector2.Zero, ViewWidth);

        Assert.Equal((0f, 0f), (GainOf(mixer, lost), PanOf(mixer, lost)));
        Assert.True(GainOf(mixer, near) > 0f);

        mixer.Settle(new Vector2(float.NaN, 0f), ViewWidth);

        Assert.Equal((0f, 0f), (GainOf(mixer, near), PanOf(mixer, near)));

        // An empty view keeps the last listener. A width that is not finite drops it.
        foreach (float width in (ReadOnlySpan<float>)[float.NaN, float.NegativeInfinity, float.PositiveInfinity])
        {
            mixer.Settle(Vector2.Zero, ViewWidth);
            mixer.Settle(Vector2.Zero, 0f);
            Assert.True(GainOf(mixer, near) > 0f);

            mixer.Settle(Vector2.Zero, width);

            Assert.Equal((0f, 0f), (GainOf(mixer, near), PanOf(mixer, near)));
        }
    }

    // The voice's own pan returns when its range is taken away, and gives way when one is given back.
    [Fact]
    public void ChangingARange_RaisesTheVoicesOwnPanOrItsPositionalOne()
    {
        AudioMixer mixer = Heard(Vector2.Zero);
        Voice voice = mixer.Play(new AudioPlayback(AudioMixerFixtures.Theme)
        {
            Loop = true,
            Pan = -1f,
            Range = new AudioRange(100f),
        });
        Assert.Equal(0f, PanOf(mixer, voice));

        mixer.SetRange(voice, default);
        Assert.Equal(-1f, PanOf(mixer, voice));

        mixer.SetRange(voice, new AudioRange(100f));
        Assert.Equal(0f, PanOf(mixer, voice));
    }

    // Nothing has been heard from the new scene's camera when its sources start, even on a run whose
    // last scene settled a listener. Their first gain is 0, and the scene's first step brings them in.
    [Fact]
    public void APositionalSourceStartedWithTheScene_PlaysSilentUntilTheFirstStep()
    {
        Run earlier = new();
        earlier.Audio.Settle(Vector2.Zero, ViewWidth);
        using SimulationHost run = Simulate(Vector2.Zero, Positional(new AudioRange(160f)), out Voice voice, earlier);
        Assert.Equal(0f, GainOf(run.Run.Audio, voice));

        run.Step();

        Assert.Equal(1f, GainOf(run.Run.Audio, voice));
    }

    [Fact]
    public void AStillPositionalVoice_RaisesNothingOnLaterSteps()
    {
        using SimulationHost run = Simulate(new Vector2(40f, 30f), Positional(new AudioRange(160f)), out _);
        run.Step(2);

        run.Step();

        Assert.Empty(AudioMixerFixtures.Raised(run.Run.Audio));
    }

    // A mixer whose listener has been settled at listener, across a view ViewWidth wide.
    private static AudioMixer Heard(Vector2 listener)
    {
        AudioMixer mixer = new();
        AudioMixerFixtures.Advance(mixer, 0);
        mixer.Settle(listener, ViewWidth);

        return mixer;
    }

    private static AudioSource Positional(AudioRange range) =>
        new(AudioMixerFixtures.Theme) { Loop = true, AutoPlay = true, Range = range };

    // A started scene on a new run or on, whose camera frames the origin, holding source on an entity
    // at position, and the voice the source played on starting.
    internal static SimulationHost Simulate(Vector2 position, AudioSource source, out Voice voice, Run? on = null)
    {
        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(Vector2.Zero));
        scene.Add(new SceneFixtures.Holder(position, source));
        SimulationHost run = new(scene, run: on);
        voice = LastOf(run.Run.Audio, default, AudioCommandKind.Play).Voice;

        return run;
    }

    // The gain the host was last told for voice, by its play or a later gain.
    private static float GainOf(AudioMixer mixer, Voice voice) =>
        LastOf(mixer, voice, AudioCommandKind.SetGain).Gain;

    // The pan the host was last told for voice, by its play or a later pan.
    private static float PanOf(AudioMixer mixer, Voice voice) =>
        LastOf(mixer, voice, AudioCommandKind.SetPan).Pan;

    // The last command this step raised for voice of kind or its play. A default voice matches any.
    private static AudioCommand LastOf(AudioMixer mixer, Voice voice, AudioCommandKind kind)
    {
        ReadOnlySpan<AudioCommand> commands = mixer.Commands;
        for (int i = commands.Length - 1; i >= 0; i--)
        {
            if ((voice == default || commands[i].Voice == voice)
                && (commands[i].Kind == kind || commands[i].Kind == AudioCommandKind.Play))
            {
                return commands[i];
            }
        }

        throw new InvalidOperationException($"No {kind} or play was raised for the voice this step.");
    }

    private sealed class Anchor(Vector2 position) : IAudioAnchor
    {
        public Vector2 Position => position;
    }
}

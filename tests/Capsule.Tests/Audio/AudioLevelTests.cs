using Capsule.Audio;
using Capsule.Input;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Audio.AudioMixerFixtures;

namespace Capsule.Tests.Audio;

public sealed class AudioLevelTests
{
    // A voice's gain is its own volume. A bus's volume reaches the host once, as that bus's gain, which
    // the host applies after the bus's effects. No voice is re-gained when a bus changes.
    [Fact]
    public void ABusVolume_RaisesOneGainForTheBusAndNoneForItsVoices()
    {
        AudioMixer mixer = new();
        mixer.SetVolume(AudioBus.Master, 0.5f);
        mixer.SetVolume(Sfx, 0.8f);
        Assert.Equal([(AudioBus.Master, 0.5f), (Sfx, 0.8f)], BusGains(mixer));

        Advance(mixer, 1);
        mixer.Play(new AudioPlayback(Step) { Bus = Sfx, Volume = 0.5f });
        mixer.Play(Theme, Music);
        Assert.Equal(0.5f, mixer.Commands[0].Gain);

        Advance(mixer, 2);
        mixer.SetVolume(Sfx, 0.4f);
        mixer.SetVolume(AudioBus.Master, 1f);

        Assert.Equal([(AudioCommandKind.SetBusGain, Voice.None), (AudioCommandKind.SetBusGain, Voice.None)], Raised(mixer));
        Assert.Equal([(Sfx, 0.4f), (AudioBus.Master, 1f)], BusGains(mixer));
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

        Assert.Null(Assert.Single(mixer.Commands.ToArray()).Bus);
    }

    // Levelling a bus after playing into it within one step levels the bus and leaves the voice at
    // its own volume. The two are only an ordering.
    [Fact]
    public void ABusVolumeSetAfterAPlayInTheSameStep_LevelsTheBusAndNotTheVoice()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(new AudioPlayback(Theme) { Bus = Music, Volume = 0.5f, Loop = true });
        mixer.SetVolume(Music, 0.4f);

        Assert.Equal(
            [(AudioCommandKind.Play, voice), (AudioCommandKind.SetBusGain, Voice.None)],
            Raised(mixer));
        Assert.Equal(0.5f, mixer.Commands[0].Gain);
        Assert.Equal([(Music, 0.4f)], BusGains(mixer));
    }

    // Bus volumes are the run's: what a boot scene levels stands for every scene after it, and the
    // next scene's play raises no level of its own.
    [Fact]
    public void ABusVolumeOneSceneSets_StandsForAVoiceTheNextScenePlays()
    {
        static Scene Resolve(in SceneTransition target) =>
            target.SceneType == typeof(LevellingScene) ? new LevellingScene() : new MusicScene();

        using SceneHost host = new(SceneTransition.ToScene(typeof(LevellingScene), null), Resolve, new Run());

        host.Step(SceneFixtures.Step(0));

        AudioCommand played = Assert.Single(host.Run.Audio.Commands.ToArray());
        Assert.Equal(AudioCommandKind.Play, played.Kind);
        Assert.Equal(Music, played.Bus);
        Assert.Equal(1f, played.Gain);
        Assert.Equal(0.25f, host.Run.Audio.GetVolume(Music));
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

    // Volumes take [0, 1] and pans [-1, 1].
    [Theory]
    [InlineData(-1.001f, true)]
    [InlineData(-0.001f, false)]
    [InlineData(1.001f, true)]
    [InlineData(float.NaN, true)]
    public void ALevelOutsideItsRange_IsRefused(float level, bool panRefuses)
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Step);

        if (panRefuses)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetPan(voice, level));
            Assert.Throws<ArgumentOutOfRangeException>(() => mixer.Play(new AudioPlayback(Step) { Pan = level }));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetVolume(voice, level));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.SetVolume(Sfx, level));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.Play(new AudioPlayback(Step) { Volume = level }));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.UnfocusedVolume = level);
    }

    // The loss silences at the default, a new volume lands at once while unfocused, and the regain
    // restores the master volume. Each reaches the host as the master's gain alone.
    [Fact]
    public void UnfocusedVolume_ScalesTheMasterFromAWindowFocusLossUntilTheRegain()
    {
        AudioMixer mixer = new();
        mixer.SetVolume(AudioBus.Master, 0.5f);
        mixer.Play(new AudioPlayback(Theme) { Bus = Music, Loop = true });

        mixer.BeginStep(Focused(1, false));
        Assert.Equal((AudioCommandKind.SetBusGain, AudioBus.Master, 0f), Last(mixer));

        mixer.UnfocusedVolume = 0.5f;
        Assert.Equal((AudioCommandKind.SetBusGain, AudioBus.Master, 0.25f), Last(mixer));

        mixer.BeginStep(Focused(2, false));
        Assert.Empty(mixer.Commands.ToArray());

        mixer.BeginStep(Focused(3, true));
        Assert.Equal([(AudioCommandKind.SetBusGain, Voice.None)], Raised(mixer));
        Assert.Equal((AudioCommandKind.SetBusGain, AudioBus.Master, 0.5f), Last(mixer));
    }

    private static StepContext Focused(long tick, bool focused)
    {
        InputState input = new(new ActionBindings());
        input.Advance(DeviceSnapshot.Empty.WithWindowFocus(focused));

        return new StepContext(1.0 / StepContext.DefaultStepHertz, input, tick);
    }

    // The last command raised. A command raised outside a step is appended to the last step's list.
    private static (AudioCommandKind Kind, AudioBus Bus, float Gain) Last(AudioMixer mixer)
    {
        AudioCommand command = mixer.Commands[^1];

        return (command.Kind, command.Bus!, command.Gain);
    }

    private sealed class LevellingScene : Scene
    {
        protected override void OnStart() => Run.Audio.SetVolume(Music, 0.25f);

        protected override void OnStep(in StepContext context) => Run.RequestScene<MusicScene>();
    }

    private sealed class MusicScene : Scene
    {
        protected override void OnStart() => Run.Audio.Play(new AudioPlayback(Theme) { Bus = Music, Loop = true });
    }
}

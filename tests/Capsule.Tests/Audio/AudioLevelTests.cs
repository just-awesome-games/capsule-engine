using Capsule.Audio;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Audio.AudioMixerFixtures;

namespace Capsule.Tests.Audio;

public sealed class AudioLevelTests
{
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

        using SceneHost host = new(SceneTransition.ToScene(typeof(LevellingScene), null), Resolve, new Run());

        host.Step(SceneFixtures.Step(0));

        AudioCommand played = Assert.Single(host.Run.Audio.Commands.ToArray());
        Assert.Equal(AudioCommandKind.Play, played.Kind);
        Assert.Equal(Music, played.Bus);
        Assert.Equal(0.25f, played.Gain);
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
    [InlineData(-0.001f)]
    [InlineData(1.001f)]
    [InlineData(float.NaN)]
    public void UnfocusedVolume_OutsideTheUnitRange_IsRefused(float volume)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioMixer().UnfocusedVolume = volume);
    }

    [Fact]
    public void SettingUnfocusedVolume_RaisesNoCommand()
    {
        AudioMixer mixer = new();
        mixer.Play(Step);
        AudioCommand[] before = mixer.Commands.ToArray();

        Assert.Equal(0f, mixer.UnfocusedVolume);

        mixer.UnfocusedVolume = 0.5f;

        Assert.Equal(before, mixer.Commands.ToArray());
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

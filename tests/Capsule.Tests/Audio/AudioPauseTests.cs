using Capsule.Audio;
using static Capsule.Tests.Audio.AudioMixerFixtures;

namespace Capsule.Tests.Audio;

public sealed class AudioPauseTests
{
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
}

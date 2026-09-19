using Capsule.Assets;
using Capsule.Audio;
using Capsule.Runtime;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

public sealed class AudioResidencyTests
{
    // The retirement that pooling reports is what the residency count is kept on, so a sound whose
    // voice has been played, ended and played again is still held by exactly one live voice.
    [Fact]
    public void ASoundWhosePooledVoiceIsPlayedAgainIsStillRetainedByIt()
    {
        using Fixture fixture = new();
        fixture.Preload(Step);

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step));
        fixture.Apply(Command(AudioCommandKind.Stop, Slot(0, 1)));
        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 2), Step));

        FakeSound sound = Assert.Single(fixture.Backend.Loaded);
        fixture.Preload();

        Assert.False(sound.Disposed);

        fixture.Apply(Command(AudioCommandKind.Stop, Slot(0, 2)));

        Assert.True(sound.Disposed);
    }

    [Fact]
    public void AStreamedClipIsNeverResident()
    {
        using Fixture fixture = new();

        fixture.Preload(Music);
        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Music));

        Assert.Empty(fixture.Backend.Loaded);
        Assert.Equal(Music, Assert.Single(fixture.Backend.Voices).Clip);
    }

    // The device queues a resident clip whole and can only repeat all of it, so a loop that must
    // repeat a region streams the same resident sound's samples instead. It is still one load for
    // the scene, and the voice still counts against that sound's residency.
    [Fact]
    public void ALoopOfAClipCarryingARegion_StreamsTheResidentSoundsSamples()
    {
        using Fixture fixture = new();
        fixture.Preload(Theme);

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Theme, loop: true));

        FakeSound sound = Assert.Single(fixture.Backend.Loaded);
        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        Assert.True(voice.Streamed);
        Assert.Equal(0, voice.Plays);

        fixture.Preload();

        Assert.False(sound.Disposed);

        fixture.Apply(Command(AudioCommandKind.Stop, Slot(0, 1)));

        Assert.True(sound.Disposed);
    }

    // The region is the loop's alone: a one-shot of the same clip is queued whole, as every resident
    // sound is.
    [Fact]
    public void AOneShotOfAClipCarryingARegion_IsPlayedByTheDeviceAsAnyOtherResidentSoundIs()
    {
        using Fixture fixture = new();

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Theme));

        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        Assert.False(voice.Streamed);
        Assert.Equal(1, voice.Plays);
    }

    // A SoundEffectInstance starts at the front of its buffer and nowhere else, so a resident clip
    // asked for a start offset takes the streaming path the region loop takes — from the same
    // resident sound, so it is still one load and the voice still counts against its residency.
    [Fact]
    public void AResidentClipPlayedFromAnOffset_StreamsTheResidentSoundsSamples()
    {
        using Fixture fixture = new();
        fixture.Preload(Step);

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step, startSeconds: 0.25));

        FakeSound sound = Assert.Single(fixture.Backend.Loaded);
        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        Assert.True(voice.Streamed);
        Assert.Equal(0.25, voice.StartSeconds);

        fixture.Preload();
        Assert.False(sound.Disposed);

        fixture.Apply(Command(AudioCommandKind.Stop, Slot(0, 1)));
        Assert.True(sound.Disposed);
    }

    // A voice started on one scene plays on into the next, because the mixer is the run's. The sound
    // it is playing therefore cannot be released with the scene that loaded it.
    [Fact]
    public void ASoundALiveVoiceIsPlayingSurvivesTheSceneThatLoadedIt()
    {
        using Fixture fixture = new();
        fixture.Preload(Step);
        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step));

        FakeSound sound = Assert.Single(fixture.Backend.Loaded);
        fixture.Preload();

        Assert.False(sound.Disposed);

        fixture.Apply(Command(AudioCommandKind.Stop, Slot(0, 1)));

        Assert.True(sound.Disposed);
    }

    [Fact]
    public void ASoundNoVoiceIsPlayingLeavesWithItsScene()
    {
        using Fixture fixture = new();
        fixture.Preload(Step);

        FakeSound sound = Assert.Single(fixture.Backend.Loaded);
        fixture.Preload();

        Assert.True(sound.Disposed);
    }

    // The initial scene starts before the device is open, so what its start raised stands on the
    // mixer with no step behind it. A boot that only waited for the first step's commands would lose
    // it: BeginStep clears the table the host has not applied yet.
    [Fact]
    public void ASourceThatPlaysOnStart_SoundsOnTheBootThatOpensTheDevice()
    {
        StartupScene scene = new();

        using SceneHost host = new(SceneTransition.ToScene(typeof(StartupScene), null), (in SceneTransition _) => scene, new Run());
        using Fixture fixture = new();

        // The order LoadContent boots in: the scene's preloads, then whatever its start left standing.
        fixture.Preload(Step);
        fixture.Player.Apply(host.Run.Audio.Commands);

        FakeVoice sounding = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(Step, sounding.Clip);
        Assert.True(sounding.Loop);

        // Nothing carries it past here, which is what makes the boot delivery the only chance at it.
        host.Step(SceneFixtures.Step(0));

        Assert.Empty(host.Run.Audio.Commands.ToArray());
    }
}

using Capsule.Audio;
using Capsule.Runtime.Audio;
using static Capsule.Tests.Audio.AudioMixerFixtures;
using Fixture = Capsule.Tests.Audio.AudioPlaybackFixtures.Fixture;

namespace Capsule.Tests.Audio;

// A bus routed under another, from the game thread's rules to the mix thread's sum.
public sealed class BusRoutingTests
{
    private static readonly AudioBus Score = new("score", Music);

    // A bus's name is its identity. A second declaration under another parent is refused on first use.
    // A parent still null, as a static field declared below its child is, is refused as the child is made.
    [Fact]
    public void ABusNamedTwiceUnderDifferentParents_IsRefused()
    {
        AudioMixer mixer = new();
        mixer.SetVolume(Score, 0.5f);

        Assert.Throws<InvalidOperationException>(() => mixer.SetVolume(new AudioBus("score"), 0.5f));
        Assert.Throws<InvalidOperationException>(() => mixer.GetVolume(new AudioBus("score")));
        Assert.Throws<InvalidOperationException>(() => mixer.GetVolume(new AudioBus("score", new AudioBus("music", Sfx))));
        Assert.Throws<InvalidOperationException>(() => mixer.GetVolume(new AudioBus("loop", new AudioBus("loop"))));
        Assert.Throws<InvalidOperationException>(() => mixer.SetVolume(new AudioBus("loop", new AudioBus("loop", Sfx)), 0.5f));
        mixer.SetVolume(new AudioBus("loop"), 0.5f);
        mixer.SetVolume(new AudioBus("score", new AudioBus("music")), 0.5f);
        Assert.Throws<ArgumentNullException>(() => new AudioBus("orphan", null!));
        Assert.Throws<ArgumentException>(() => new AudioBus("master"));
    }

    // A refused declaration leaves the voice table as it was, even with every slot taken, and a play that finds
    // no slot registers nothing.
    [Fact]
    public void APlayOnARefusedBus_StealsNoVoice()
    {
        AudioMixer mixer = new();
        mixer.SetVolume(Music, 1f);
        Voice[] voices = [.. Enumerable.Range(0, AudioMixer.MaxVoices).Select(_ => mixer.Play(Step))];

        Assert.Throws<InvalidOperationException>(() => mixer.Play(Step, new AudioBus("music", Sfx)));
        Assert.All(voices, voice => Assert.True(mixer.IsLive(voice)));

        AudioMixer looping = new();
        for (int i = 0; i < AudioMixer.MaxVoices; i++)
        {
            looping.Play(new AudioPlayback(Theme) { Loop = true });
        }

        looping.Play(Step, new AudioBus("unheard", Sfx));
        looping.SetVolume(new AudioBus("unheard"), 0.5f);
    }

    // A pause holds every voice under the bus, and the child's own pause state stays its own.
    [Fact]
    public void PausingAParent_HoldsTheVoicesOnItsChildren()
    {
        AudioMixer mixer = new();
        Voice voice = mixer.Play(Theme, Score);

        mixer.Pause(Music);
        Assert.True(mixer.IsPaused(voice));
        Assert.False(mixer.IsPaused(Score));

        mixer.Resume(Music);
        Assert.True(mixer.IsPlaying(voice));
    }

    // The host meets the parent before the child. A parent's index is always below its children's.
    [Fact]
    public void AChildsFirstCommand_RegistersItsParentOnTheHostFirst()
    {
        using Fixture fixture = new();
        fixture.Apply(new AudioCommand(AudioCommandKind.SetBusGain, Voice.None, Bus: Score, Gain: 0.5f));

        MixBus[] buses = fixture.Posted[^2].Buses!;
        Assert.Equal([0, 1], buses.Select(static bus => bus.Parent));
    }

    // A child's sound passes through its own gain and then its parent's. The two multiply.
    [Fact]
    public void AChild_AddsIntoItsParent_WhichScalesTheSum()
    {
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = AudioPlaybackFixtures.Mixer();
        commands.TryWrite(new MixCommand(MixCommandKind.Buses, 0, 0) { Buses = [new MixBus(), new MixBus(parent: 1)] });
        commands.TryWrite(new MixCommand(MixCommandKind.SetBusGain, 0, 0, Gain: 0.5f) { Bus = 1 });
        commands.TryWrite(new MixCommand(MixCommandKind.SetBusGain, 0, 0, Gain: 0.5f) { Bus = 2 });

        // The first block ramps the gains down, and the second plays at them.
        float[] block = new float[VoiceMixer.BlockFrames * 2];
        mixer.Mix(block);
        float[] impulse = new float[VoiceMixer.BlockFrames * 2];
        impulse[0] = impulse[1] = 1f;
        commands.TryWrite(AudioPlaybackFixtures.Play(new PcmAudio(impulse, 2, VoiceMixer.SampleRate), bus: 2));
        mixer.Mix(block);

        Assert.Equal(0.25f, block[0], 1e-6f);
        Assert.Equal(0.25f, block[1], 1e-6f);
    }
}

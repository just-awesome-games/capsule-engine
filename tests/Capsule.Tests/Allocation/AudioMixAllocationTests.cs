using Capsule.Assets;
using Capsule.Audio;
using Capsule.Runtime.Audio;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class AudioMixAllocationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // Every voice sounding: resident clips of both widths and layouts at varied pitch, on the master
    // bus and on another, looping regions that wrap many times over the measured blocks, one voice
    // held silent, a stream decoded on its worker, and gain changes landing between blocks. The mix
    // thread's whole job allocates nothing.
    [Fact]
    public void MixingSixtyFourVoicesWithAStreamAndLoopWraps_AllocatesNothing()
    {
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = Mixer();
        commands.TryWrite(new MixCommand(MixCommandKind.Buses, 0, 0) { Buses = [new float[VoiceMixer.BlockFrames * VoiceMixer.Channels]] });

        PcmAudio[] clips =
        [
            new(Noise(2 * 4000, seed: 1), 2, 32_000),
            new(Noise(3000, seed: 2), 1, 22_050),
            new(new float[2 * 5000], 2, 44_100),
            new(new float[6000], 1, 48_000),
        ];

        for (int slot = 0; slot < AudioMixer.MaxVoices - 1; slot++)
        {
            PcmAudio clip = clips[slot % clips.Length];
            commands.TryWrite(Play(clip, 0.5f + (slot * 0.03f), loop: true, loopStart: 100, loopEnd: clip.Frames - 100, bus: slot % 2, slot: slot, gain: slot == 7 ? 0f : 0.5f));
        }

        using AudioStreamer streamer = new();
        AudioStream stream = streamer.Play(new SampleSource(new float[2 * 20_000], 2, 44_100), new AudioClip("hum", ".ogg", 1.0), loop: true);
        commands.TryWrite(new MixCommand(MixCommandKind.Play, AudioMixer.MaxVoices - 1, 1, 1f, 1.1f) { Stream = stream });

        float[] block = new float[VoiceMixer.BlockFrames * VoiceMixer.Channels];

        Assert.Equal(0, StepMeasurement.Bytes(
            index =>
            {
                commands.TryWrite(new MixCommand(MixCommandKind.SetGain, index % (AudioMixer.MaxVoices - 1), 1, Gain: 0.25f + ((index % 3) * 0.25f)));
                mixer.Mix(block);
            },
            warmup: 20,
            measured: 400));
    }

    // With the voice table, the bus and a stream already met, playing, retuning and stopping from the
    // game thread allocates nothing there. A stream's replay comes from the pool, and the source here
    // is one object a test can arm again, where a run opens a file.
    [Fact]
    public void WarmPlaysStopsAndGainChangesFromTheGameThread_AllocateNothing()
    {
        SampleSource source = new(new float[2 * 20_000], 2, 44_100);
        AudioClip music = new("hum", ".ogg", 1.0);
        AudioBus sfx = new("sfx");
        AudioRing<MixCommand> commands = new(AudioPlayer.CommandCapacity);
        AudioRing<VoiceEnded> ended = new(AudioPlayer.EndedCapacity);
        using AudioStreamer streamer = new();
        using SoundStore store = new(static clip => new PcmAudio(new short[8000], 1, 8000), _ => source);
        AudioPlayer player = new(store, streamer, commands, ended);
        VoiceMixer mixer = new(commands, ended);
        float[] block = new float[VoiceMixer.BlockFrames * VoiceMixer.Channels];
        Func<bool> pooled = () => streamer.Pooled == 1;

        AssetCollection preloads = new();
        preloads.Add(Step);
        store.ChangeScene(preloads);

        AudioCommand[] resident =
        [
            Command(AudioCommandKind.Play, Slot(0, 1), Step, bus: sfx),
            Command(AudioCommandKind.SetGain, Slot(0, 1), gain: 0.5f),
            Command(AudioCommandKind.SetPitch, Slot(0, 1), pitch: 1.5f),
            Command(AudioCommandKind.Stop, Slot(0, 1)),
        ];
        AudioCommand[] streamed =
        [
            Command(AudioCommandKind.Play, Slot(1, 1), music, loop: true),
            Command(AudioCommandKind.Stop, Slot(1, 1)),
        ];

        // The stream's first play builds it. Its retirement through the mixer's report pools it.
        player.Apply(resident);
        player.Apply(streamed);
        mixer.Mix(block);
        player.Update();
        Assert.True(SpinWait.SpinUntil(pooled, Patience), "the stream did not return to the pool in time");

        long before = GC.GetAllocatedBytesForCurrentThread();
        player.Apply(resident);
        player.Apply(streamed);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(0, streamer.Pooled);
    }
}

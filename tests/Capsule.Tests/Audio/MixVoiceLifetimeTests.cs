using Capsule.Runtime.Audio;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

// How the host's mix starts, holds, replaces and ends a voice, driven block by block with no device
// and no thread.
public sealed class MixVoiceLifetimeTests
{
    private const int Fade = VoiceMixer.FadeFrames;
    private const int Block = VoiceMixer.BlockFrames;

    // A voice played onto a paused bus arrives as a play and a pause together. Heard, it would sound a
    // fade's worth of its first frames. Silent, it would pass a whole block and end before it held.
    [Theory]
    [InlineData(1f)]
    [InlineData(0f)]
    public void AVoicePausedAsItStarts_NeitherSoundsNorAdvancesUntilResumed(float gain)
    {
        PcmAudio clip = new(Noise(2 * 300, seed: 5), 2, VoiceMixer.SampleRate);
        (VoiceMixer mixer, AudioRing<MixCommand> commands, AudioRing<VoiceEnded> ended) = Mixer();
        commands.TryWrite(Play(clip, gain: gain));
        commands.TryWrite(new MixCommand(MixCommandKind.Pause, 0, 1));

        float[] block = new float[Block * VoiceMixer.Channels];
        for (int i = 0; i < 3; i++)
        {
            mixer.Mix(block);
            Assert.All(block, static sample => Assert.Equal(0f, sample));
        }

        Assert.False(ended.TryRead(out _));

        commands.TryWrite(new MixCommand(MixCommandKind.SetGain, 0, 1, Gain: 1f));
        commands.TryWrite(new MixCommand(MixCommandKind.Resume, 0, 1));
        mixer.Mix(block);

        Assert.Contains(block, static sample => sample != 0f);
        Assert.True(ended.TryRead(out VoiceEnded report));
        Assert.Equal((0, 1), (report.Slot, report.Generation));
    }

    // Held after a block and a pause's fade, the voice is silent for blocks, and a resume fades in
    // from the frame it held on.
    [Fact]
    public void AHeldVoice_NeitherSoundsNorAdvancesAndResumesFromItsHeldFrame()
    {
        short[] clip = Noise(2 * 4000, seed: 6);
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = Mixer();
        commands.TryWrite(Play(new PcmAudio(clip, 2, VoiceMixer.SampleRate)));
        float[] block = new float[Block * VoiceMixer.Channels];
        mixer.Mix(block);

        commands.TryWrite(new MixCommand(MixCommandKind.Pause, 0, 1));
        mixer.Mix(block);
        for (int i = 0; i < 3; i++)
        {
            mixer.Mix(block);
            Assert.All(block, static sample => Assert.Equal(0f, sample));
        }

        commands.TryWrite(new MixCommand(MixCommandKind.Resume, 0, 1));
        mixer.Mix(block);

        const int Held = Block + Fade;
        float[] expected = new float[block.Length];
        for (int sample = 0; sample < expected.Length; sample++)
        {
            int frame = sample / 2;
            float envelope = frame < Fade ? (float)frame / Fade : 1f;
            expected[sample] = clip[(2 * Held) + sample] * PcmAudio.Pcm16Scale * (1f * envelope);
        }

        Assert.Equal(expected, block);
    }

    // The steal's stop fades the old voice out beside the new one in the same block, and each is
    // reported ended once.
    [Fact]
    public void AStealWhileAudible_FadesTheOldVoiceBesideTheNewAndReportsEachOnce()
    {
        (VoiceMixer mixer, AudioRing<MixCommand> commands, AudioRing<VoiceEnded> ended) = Mixer();
        commands.TryWrite(Play(Level(16384), loop: true));
        float[] block = new float[Block * VoiceMixer.Channels];
        mixer.Mix(block);

        commands.TryWrite(new MixCommand(MixCommandKind.Stop, 0, 1));
        commands.TryWrite(Play(Level(8192), loop: true) with { Generation = 2 });
        mixer.Mix(block);

        float[] expected = new float[block.Length];
        for (int frame = 0; frame < Block; frame++)
        {
            float old = frame < Fade ? 0.5f * (1f * ((float)(Fade - 1 - frame) / Fade)) : 0f;
            expected[2 * frame] = expected[(2 * frame) + 1] = (0.25f * (1f * 1f)) + old;
        }

        Assert.Equal(expected, block);
        Assert.True(ended.TryRead(out VoiceEnded first));
        Assert.Equal((0, 1), (first.Slot, first.Generation));

        commands.TryWrite(new MixCommand(MixCommandKind.Stop, 0, 2));
        mixer.Mix(block);
        mixer.Mix(block);

        Assert.True(ended.TryRead(out VoiceEnded second));
        Assert.Equal((0, 2), (second.Slot, second.Generation));
        Assert.False(ended.TryRead(out _));
    }

    // The game expires a one-shot on a step the mix has not reached the end of. A play to its slot with
    // no stop lets its last samples play out untouched beside the new voice.
    [Fact]
    public void AnExpiredOneShotsSlotReplayed_PlaysItsLastSamplesUnchanged()
    {
        const int Frames = 700;
        short[] clip = Noise(2 * Frames, seed: 7);
        (VoiceMixer mixer, AudioRing<MixCommand> commands, AudioRing<VoiceEnded> ended) = Mixer();
        commands.TryWrite(Play(new PcmAudio(clip, 2, VoiceMixer.SampleRate)));
        float[] block = new float[Block * VoiceMixer.Channels];
        mixer.Mix(block);

        commands.TryWrite(Play(Level(0), loop: true) with { Generation = 2 });
        mixer.Mix(block);

        float[] expected = new float[block.Length];
        for (int sample = 0; sample < 2 * (Frames - Block); sample++)
        {
            expected[sample] = clip[(2 * Block) + sample] * PcmAudio.Pcm16Scale * (1f * 1f);
        }

        Assert.Equal(expected, block);
        Assert.True(ended.TryRead(out VoiceEnded report));
        Assert.Equal((0, 1), (report.Slot, report.Generation));
    }

    // A stereo clip at the output's rate holding one value throughout.
    private static PcmAudio Level(short value)
    {
        short[] level = new short[2 * 4096];
        Array.Fill(level, value);

        return new PcmAudio(level, 2, VoiceMixer.SampleRate);
    }
}

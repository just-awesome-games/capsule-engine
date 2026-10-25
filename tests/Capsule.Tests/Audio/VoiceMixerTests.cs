using Capsule.Runtime.Audio;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

// The host's mix against itself: no device and no thread, the mixer driven block by block as the
// mix thread drives it.
public sealed class VoiceMixerTests
{
    private const int Fade = VoiceMixer.FadeFrames;

    // At the output's rate, unit pitch and unit gain a stereo voice is its samples, bit for bit, and
    // a mono one is its samples at cos(pi/4) on each side. A start at frame 0 does not fade in.
    [Theory]
    [InlineData(2, 1f)]
    [InlineData(1, 0.70710677f)]
    public void AUnitStepAtUnitGainAndCentrePan_PassesEverySampleThrough(int channels, float side)
    {
        const int Frames = 1000;
        short[] clip = Noise((Frames + 10) * channels, seed: 1);
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = Mixer();
        commands.TryWrite(Play(new PcmAudio(clip, channels, VoiceMixer.SampleRate)));

        float[] output = new float[Frames * VoiceMixer.Channels];
        mixer.Mix(output);

        float[] expected = new float[output.Length];
        for (int frame = 0; frame < Frames; frame++)
        {
            for (int channel = 0; channel < VoiceMixer.Channels; channel++)
            {
                short sample = clip[(frame * channels) + (channels == 2 ? channel : 0)];
                expected[(frame * VoiceMixer.Channels) + channel] = sample * PcmAudio.Pcm16Scale * side;
            }
        }

        Assert.Equal(expected, output);
    }

    // A stream long enough to wrap its ring reaches the output exactly as its decoder read it. Each
    // block waits for the worker. A starved block would be a test of the worker's pace, not the path.
    [Fact]
    public void AStreamedVoiceAtUnitStepAndGain_PassesItsDecodedSamplesThrough()
    {
        const int Frames = 24 * VoiceMixer.BlockFrames;
        short[] noise = Noise(2 * Frames, seed: 8);
        float[] decoded = new float[noise.Length];
        for (int i = 0; i < decoded.Length; i++)
        {
            decoded[i] = noise[i] * PcmAudio.Pcm16Scale;
        }

        using AudioStreamer streamer = new();
        AudioStream stream = streamer.Play(new SampleSource(decoded, 2, VoiceMixer.SampleRate), Music, loop: false);
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = Mixer();
        commands.TryWrite(new MixCommand(MixCommandKind.Play, 0, 1, 1f) { Stream = stream });

        float[] output = new float[decoded.Length];
        for (int at = 0; at < output.Length; at += 2 * VoiceMixer.BlockFrames)
        {
            Assert.True(
                SpinWait.SpinUntil(() => stream.Ended || stream.WrittenSamples - stream.ReadPosition >= 2 * VoiceMixer.BlockFrames, TimeSpan.FromSeconds(10)),
                "the worker did not decode in time");
            mixer.Mix(output.AsSpan(at, 2 * VoiceMixer.BlockFrames));
        }

        Assert.Equal(decoded, output);
        Assert.Equal(0, mixer.Underruns);
    }

    // The loop seam: a region repeated in the mix is exactly the same spline run over the region's
    // samples joined end to end, at a rate and a pitch that put the seam between output frames. The
    // loop plays on a bus of its own, and the bus sums into the master untouched.
    [Fact]
    public void ALoopRegion_MixesExactlyAsItsSamplesJoinedEndToEnd()
    {
        const int Rate = 32_000;
        const float Pitch = 1.37f;
        const int Start = 700;
        const int End = 2100;
        short[] clip = Noise(2 * 3000, seed: 2);

        List<short> joined = [.. clip.AsSpan(0, 2 * End)];
        for (int repeat = 0; repeat < 6; repeat++)
        {
            joined.AddRange(clip.AsSpan(2 * Start, 2 * (End - Start)));
        }

        (VoiceMixer looped, AudioRing<MixCommand> loopCommands, _) = Mixer();
        loopCommands.TryWrite(new MixCommand(MixCommandKind.Buses, 0, 0) { Buses = [new float[VoiceMixer.BlockFrames * VoiceMixer.Channels]] });
        loopCommands.TryWrite(Play(new PcmAudio(clip, 2, Rate), Pitch, loop: true, loopStart: Start, loopEnd: End, bus: 1));

        (VoiceMixer straight, AudioRing<MixCommand> straightCommands, _) = Mixer();
        straightCommands.TryWrite(Play(new PcmAudio([.. joined], 2, Rate), Pitch));

        // Past three repeats of the region.
        float[] loop = new float[8 * VoiceMixer.BlockFrames * VoiceMixer.Channels];
        float[] reference = new float[loop.Length];
        looped.Mix(loop);
        straight.Mix(reference);

        Assert.Equal(reference, loop);
        Assert.Contains(loop, static sample => sample != 0f);
    }

    [Fact]
    public void AStop_FadesToExactlyZeroAndTheVoiceThenProducesNothing()
    {
        short[] level = new short[2 * 4096];
        Array.Fill(level, (short)16384);
        (VoiceMixer mixer, AudioRing<MixCommand> commands, AudioRing<VoiceEnded> ended) = Mixer();
        commands.TryWrite(Play(new PcmAudio(level, 2, VoiceMixer.SampleRate), loop: true));
        float[] block = new float[VoiceMixer.BlockFrames * VoiceMixer.Channels];
        mixer.Mix(block);

        commands.TryWrite(new MixCommand(MixCommandKind.Stop, 0, 1));
        mixer.Mix(block);

        float[] expected = new float[block.Length];
        for (int frame = 0; frame < Fade; frame++)
        {
            expected[2 * frame] = expected[(2 * frame) + 1] = 0.5f * (1f * ((float)(Fade - 1 - frame) / Fade));
        }

        Assert.Equal(expected, block);
        Assert.Equal(0f, block[2 * (Fade - 1)]);
        Assert.True(ended.TryRead(out VoiceEnded report));
        Assert.Equal((0, 1), (report.Slot, report.Generation));

        mixer.Mix(block);

        Assert.All(block, static sample => Assert.Equal(0f, sample));
        Assert.False(ended.TryRead(out _));
    }

    // A silent voice skips the spline but not its time. Brought back up after blocks at gain 0, it
    // plays exactly what a voice heard all along plays, past its loop's wraps and once the gain's
    // ramp has run its block.
    [Fact]
    public void AVoiceAtGainZeroForBlocks_ResumesAtTheSampleItsTimeSays()
    {
        PcmAudio clip = new(Noise(2 * 1000, seed: 3), 2, 32_000);
        (VoiceMixer heard, AudioRing<MixCommand> heardCommands, _) = Mixer();
        (VoiceMixer silent, AudioRing<MixCommand> silentCommands, _) = Mixer();
        heardCommands.TryWrite(Play(clip, 1.37f, loop: true));
        silentCommands.TryWrite(Play(clip, 1.37f, loop: true, gain: 0f));

        float[] heardBlock = new float[VoiceMixer.BlockFrames * VoiceMixer.Channels];
        float[] silentBlock = new float[heardBlock.Length];
        for (int block = 0; block < 5; block++)
        {
            heard.Mix(heardBlock);
            silent.Mix(silentBlock);
            Assert.All(silentBlock, static sample => Assert.Equal(0f, sample));
        }

        silentCommands.TryWrite(new MixCommand(MixCommandKind.SetGain, 0, 1, Gain: 1f));
        heard.Mix(heardBlock);
        silent.Mix(silentBlock);

        heard.Mix(heardBlock);
        silent.Mix(silentBlock);

        Assert.Equal(heardBlock, silentBlock);
    }

    // A start mid-clip cuts into a waveform, and fades in over the fade from the frame it names.
    [Fact]
    public void AStartAtAnOffset_BeginsOnItsFrameAndFadesIn()
    {
        const int Offset = 300;
        short[] clip = Noise(2 * 2000, seed: 4);
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = Mixer();
        commands.TryWrite(Play(new PcmAudio(clip, 2, VoiceMixer.SampleRate), startFrame: Offset));

        float[] output = new float[VoiceMixer.BlockFrames * VoiceMixer.Channels];
        mixer.Mix(output);

        float[] expected = new float[output.Length];
        for (int sample = 0; sample < expected.Length; sample++)
        {
            int frame = sample / 2;
            float envelope = frame < Fade ? (float)frame / Fade : 1f;
            expected[sample] = clip[(2 * Offset) + sample] * PcmAudio.Pcm16Scale * (1f * envelope);
        }

        Assert.Equal(expected, output);
    }
}

using Capsule.Audio;
using Capsule.Runtime.Audio;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

// A bus's effects and gain on the mix thread, driven block by block with no device or thread.
public sealed class BusEffectsMixTests
{
    private const int Rate = VoiceMixer.SampleRate;
    private const int Block = VoiceMixer.BlockFrames;

    // An impulse comes back every delay at the echo's volume times a power of its feedback, and keeps
    // coming back after the voice that played it is stopped. Inverted on the left, the left side's
    // repeats come back negated.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEcho_RepeatsAnImpulseAtItsDelayAndRingsOnAfterItsVoiceIsCut(bool invertLeft)
    {
        const int Delay = 300;
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = OnBus(out _);
        commands.TryWrite(Effects(EffectLevels.None with { EchoDelaySeconds = Delay / (float)Rate, EchoFeedback = 0.5f, EchoVolume = 0.8f, EchoInvertLeft = invertLeft }, Delay));
        commands.TryWrite(Play(Impulse(4 * Block), bus: 1));

        float[] output = new float[4 * Block * 2];
        mixer.Mix(output.AsSpan(0, Block * 2));
        commands.TryWrite(new MixCommand(MixCommandKind.Stop, 0, 1));
        mixer.Mix(output.AsSpan(Block * 2));

        float[] expected = new float[output.Length];
        expected[0] = expected[1] = 1f;
        for (int repeat = 1; repeat * Delay < 4 * Block; repeat++)
        {
            float level = 0.8f * MathF.Pow(0.5f, repeat - 1);
            expected[2 * repeat * Delay] = invertLeft ? -level : level;
            expected[(2 * repeat * Delay) + 1] = level;
        }

        Assert.Equal(expected, output, (a, b) => MathF.Abs(a - b) < 1e-6f);
    }

    // Turning InvertLeft on swings the repeats' left side to the other sign across one block, through 0.
    // A ringing echo never steps.
    [Fact]
    public void InvertLeftTurnedOn_SwingsTheRepeatsLeftSideAcrossOneBlock()
    {
        const int Delay = 300;
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = OnBus(out _);
        EffectLevels echo = EffectLevels.None with { EchoDelaySeconds = Delay / (float)Rate, EchoFeedback = 0.9f, EchoVolume = 1f };
        commands.TryWrite(Effects(echo, Delay));
        commands.TryWrite(Play(Impulse(Block), bus: 1));

        float[] block = new float[Block * 2];
        mixer.Mix(block);
        commands.TryWrite(Effects(echo with { EchoInvertLeft = true }));
        mixer.Mix(block);

        // The repeats at 600 and 900 land 88 and 388 frames into the swing.
        Assert.Equal((0.9f * (1f - (2f * 88 / Block)), 0.9f), (block[2 * 88], block[(2 * 88) + 1]), FloatPair);
        Assert.Equal((0.81f * (1f - (2f * 388 / Block)), 0.81f), (block[2 * 388], block[(2 * 388) + 1]), FloatPair);

        mixer.Mix(block);
        Assert.Equal((-0.729f, 0.729f), (block[2 * (1200 - (2 * Block))], block[(2 * (1200 - (2 * Block))) + 1]), FloatPair);
    }

    // The bus's gain applies after its effects: muting the bus silences its echo, and the echo kept ringing
    // unheard comes back with the gain.
    [Fact]
    public void ABusGainOfZero_SilencesItsEchoWhichRingsOnUnheard()
    {
        const int Delay = 400;
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = OnBus(out _);
        commands.TryWrite(Effects(EffectLevels.None with { EchoDelaySeconds = Delay / (float)Rate, EchoFeedback = 0.9f, EchoVolume = 1f }, Delay));
        commands.TryWrite(Play(Impulse(Block), bus: 1));

        float[] block = new float[Block * 2];
        mixer.Mix(block);
        commands.TryWrite(new MixCommand(MixCommandKind.SetBusGain, 0, 0, Gain: 0f) { Bus = 1 });
        mixer.Mix(block);

        for (int i = 0; i < 4; i++)
        {
            mixer.Mix(block);
            Assert.All(block, static sample => Assert.Equal(0f, sample));
        }

        commands.TryWrite(new MixCommand(MixCommandKind.SetBusGain, 0, 0, Gain: 1f) { Bus = 1 });
        mixer.Mix(block);
        mixer.Mix(block);
        Assert.Contains(block, static sample => sample != 0f);
    }

    // A voice on a muted bus is not mixed but keeps its time. Brought back up, it plays exactly what a
    // voice heard all along plays once the gain's ramp has run its block.
    [Fact]
    public void AVoiceOnAMutedBus_ResumesAtTheSampleItsTimeSays()
    {
        PcmAudio clip = new(Noise(2 * 1000, seed: 5), 2, 32_000);
        (VoiceMixer heard, AudioRing<MixCommand> heardCommands, _) = OnBus(out _);
        (VoiceMixer muted, AudioRing<MixCommand> mutedCommands, _) = OnBus(out MixBus bus);
        heardCommands.TryWrite(Play(clip, 1.37f, loop: true, bus: 1));
        mutedCommands.TryWrite(new MixCommand(MixCommandKind.SetBusGain, 0, 0, Gain: 0f) { Bus = 1 });
        mutedCommands.TryWrite(Play(clip, 1.37f, loop: true, bus: 1));

        float[] heardBlock = new float[Block * 2];
        float[] mutedBlock = new float[heardBlock.Length];
        for (int i = 0; i < 5; i++)
        {
            heard.Mix(heardBlock);
            muted.Mix(mutedBlock);
        }

        Assert.True(bus.Muted);
        mutedCommands.TryWrite(new MixCommand(MixCommandKind.SetBusGain, 0, 0, Gain: 1f) { Bus = 1 });
        heard.Mix(heardBlock);
        muted.Mix(mutedBlock);
        heard.Mix(heardBlock);
        muted.Mix(mutedBlock);

        Assert.Equal(heardBlock, mutedBlock);
    }

    // A 12 dB per octave response: unity well inside the pass band, half power at the cutoff, and
    // about a sixteenth two octaves into the stop band.
    [Theory]
    [InlineData(false, 100f, 1f)]
    [InlineData(false, 1000f, 0.7071f)]
    [InlineData(false, 4000f, 0.0598f)]
    [InlineData(true, 4000f, 0.9985f)]
    [InlineData(true, 1000f, 0.7071f)]
    [InlineData(true, 250f, 0.0622f)]
    public void AFilter_PassesASineAtTheGainItsCutoffGives(bool highPass, float hertz, float gain)
    {
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = OnBus(out _);
        EffectLevels levels = highPass ? EffectLevels.None with { HighPassHertz = 1000f } : EffectLevels.None with { LowPassHertz = 1000f };
        commands.TryWrite(Effects(levels));
        commands.TryWrite(Play(Sine(hertz, Rate / 2), bus: 1));

        float[] output = new float[Rate / 2 * 2];
        mixer.Mix(output);

        // Past the filter's settling, over whole cycles of every frequency here.
        float rms = Rms(output.AsSpan(output.Length / 2));
        Assert.InRange(rms / (0.5f / MathF.Sqrt(2f)), gain * 0.97f, (gain * 1.03f) + 0.002f);
    }

    // A cutoff swept down and back across a sine, out of the filter's bypass and into it again, moves the
    // output no faster than the sine itself moves: the sweep never clicks.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASweptCutoff_NeverJumps(bool highPass)
    {
        const int Blocks = 40;
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = OnBus(out _);
        commands.TryWrite(Play(Sine(440f, (Blocks + 2) * Block), bus: 1));

        float[] output = new float[Blocks * Block * 2];
        for (int i = 0; i < Blocks; i++)
        {
            // Out to 2 kHz over 20 blocks and back to the off end over the next 19, in octaves.
            float travel = i < 20 ? i / 19f : Math.Max(0f, (38 - i) / 18f);
            float off = highPass ? AudioEffects.LowestHertz : AudioEffects.HighestHertz;
            float hertz = travel == 0f ? off : MathF.Pow(2f, float.Lerp(MathF.Log2(off), MathF.Log2(2000f), travel));
            commands.TryWrite(Effects(highPass ? EffectLevels.None with { HighPassHertz = hertz } : EffectLevels.None with { LowPassHertz = hertz }));
            mixer.Mix(output.AsSpan(i * Block * 2, Block * 2));
        }

        float steepest = 2f * MathF.PI * 440f / Rate * 0.5f;
        for (int at = 2; at < output.Length; at += 2)
        {
            Assert.True(MathF.Abs(output[at] - output[at - 2]) < steepest * 1.3f, $"the output jumped at frame {at / 2}");
        }
    }

    // Once the repeats have died away, flushed to exact zeros through the cuts in the loop, the echo is
    // idle: silence in gives silence out, and the bus reports nothing ringing.
    [Fact]
    public void AnEchoWhoseRepeatsHaveDiedAway_GoesIdle()
    {
        const int Delay = 480;
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = OnBus(out MixBus bus);
        commands.TryWrite(Effects(
            EffectLevels.None with { EchoDelaySeconds = Delay / (float)Rate, EchoFeedback = 0.5f, EchoVolume = 1f, EchoLowCutHertz = 200f, EchoHighCutHertz = 5000f },
            Delay));
        commands.TryWrite(Play(Impulse(Block), bus: 1));

        float[] block = new float[Block * 2];
        mixer.Mix(block);
        Assert.True(bus.Ringing);

        int blocks = 0;
        while (bus.Ringing && blocks++ < 400)
        {
            mixer.Mix(block);
        }

        Assert.False(bus.Ringing);
        mixer.Mix(block);
        Assert.All(block, static sample => Assert.Equal(0f, sample));
    }

    // A changed delay starts an empty line, and the old one's repeats fade out within the fade frames from
    // the volume last heard. The old repeat due 88 frames into the next block sounds at 7 / 96 of its 0.9.
    // Changes that land together are one change: a second delay replaces the empty line in place, and a
    // volume set in the same breath was never heard.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AChangedDelay_FadesTheOldRepeatsOutWithinTheFade(int row)
    {
        const int Delay = 300;
        EffectLevels echo = EffectLevels.None with { EchoDelaySeconds = Delay / (float)Rate, EchoFeedback = 0.9f, EchoVolume = 1f };
        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = OnBus(out _);
        commands.TryWrite(Effects(echo, Delay));
        commands.TryWrite(Play(Impulse(Block), bus: 1));

        float[] block = new float[Block * 2];
        mixer.Mix(block);

        if (row == 2)
        {
            commands.TryWrite(Effects(echo with { EchoVolume = 0.2f }));
        }

        commands.TryWrite(Effects(echo with { EchoDelaySeconds = 200f / Rate }, 200));
        if (row == 1)
        {
            commands.TryWrite(Effects(echo with { EchoDelaySeconds = 250f / Rate }, 250));
        }

        mixer.Mix(block);

        Assert.Equal(0.9f * 7f / VoiceMixer.FadeFrames, block[2 * 88], 1e-6f);
        Assert.All(block.AsSpan(2 * VoiceMixer.FadeFrames).ToArray(), static sample => Assert.Equal(0f, sample));
        mixer.Mix(block);
        Assert.All(block, static sample => Assert.Equal(0f, sample));
    }

    // A filter fading to its off end lands there without a step, though neither end is the dry signal:
    // a 20 Hz high-pass holds back a constant, and a 20 kHz low-pass holds back the highest frequency a
    // block can carry. The output's level moves no faster than a block-long crossfade to dry moves it.
    // A filter turned back on in the very next block starts from dry, as one leaving bypass does.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AFilterFadedToItsOffEnd_ReachesTheDrySignalWithoutAStep(bool highPass, bool turnedBackOn)
    {
        const int Blocks = 40;
        float[] samples = new float[(Blocks + 2) * Block * 2];
        for (int frame = 0; frame < samples.Length / 2; frame++)
        {
            samples[2 * frame] = samples[(2 * frame) + 1] = highPass || frame % 2 == 0 ? 0.5f : -0.5f;
        }

        (VoiceMixer mixer, AudioRing<MixCommand> commands, _) = OnBus(out _);
        commands.TryWrite(Play(new PcmAudio(samples, 2, Rate), bus: 1));

        float[] output = new float[Blocks * Block * 2];
        float off = highPass ? AudioEffects.LowestHertz : AudioEffects.HighestHertz;
        float start = highPass ? 200f : 2000f;
        for (int i = 0; i < Blocks; i++)
        {
            // Held for 20 blocks to settle, then out to the off end over 10, in octaves.
            float travel = Math.Clamp((i - 20) / 10f, 0f, 1f);
            float hertz = travel == 1f ? off : MathF.Pow(2f, float.Lerp(MathF.Log2(start), MathF.Log2(off), travel));
            if (turnedBackOn && i > 30)
            {
                hertz = 40f;
            }

            commands.TryWrite(Effects(highPass ? EffectLevels.None with { HighPassHertz = hertz } : EffectLevels.None with { LowPassHertz = hertz }));
            mixer.Mix(output.AsSpan(i * Block * 2, Block * 2));
        }

        // Block 30 lands, and its last frame is dry.
        Assert.Equal(0.5f, MathF.Abs(output[(31 * Block * 2) - 2]), 1e-6f);
        for (int at = 20 * Block * 2; at < output.Length; at += 2)
        {
            Assert.True(MathF.Abs(MathF.Abs(output[at]) - MathF.Abs(output[at - 2])) < 0.01f, $"the output stepped at frame {at / 2}");
        }
    }

    // A mixer with one bus besides the master, at index 1.
    private static MixCommand Effects(in EffectLevels levels, int lineFrames = 0) =>
        new(MixCommandKind.SetBusEffects, 0, 0)
        {
            Bus = 1,
            Effects = levels,
            Line = lineFrames > 0 ? new float[lineFrames * VoiceMixer.Channels] : null,
        };

    // A stereo clip at the output's rate: 1 on its first frame and silence after.
    private static PcmAudio Impulse(int frames)
    {
        float[] samples = new float[frames * 2];
        samples[0] = samples[1] = 1f;

        return new PcmAudio(samples, 2, Rate);
    }

    // A stereo sine at amplitude 0.5 at the output's rate.
    private static PcmAudio Sine(float hertz, int frames)
    {
        float[] samples = new float[frames * 2];
        for (int frame = 0; frame < frames; frame++)
        {
            samples[2 * frame] = samples[(2 * frame) + 1] = 0.5f * MathF.Sin(2f * MathF.PI * hertz * frame / Rate);
        }

        return new PcmAudio(samples, 2, Rate);
    }

    private static bool FloatPair((float Left, float Right) a, (float Left, float Right) b) =>
        MathF.Abs(a.Left - b.Left) < 1e-5f && MathF.Abs(a.Right - b.Right) < 1e-5f;

    private static float Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0.0;
        foreach (float sample in samples)
        {
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }
}

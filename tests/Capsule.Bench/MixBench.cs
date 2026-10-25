using System.Diagnostics;
using Capsule.Audio;
using Capsule.Runtime.Audio;

namespace Capsule.Bench;

// The Mix64 row: the host's sound mix timed one 512-frame block at a time, in this process with no
// device. Every voice sounds: 63 resident 32 kHz stereo 16-bit clips looping their regions at pitches
// spread over two octaves, which keeps every voice on the spline, and one stream its worker decodes
// ahead. A block must take far less than the 10.7 ms it plays for.
internal static class MixBench
{
    internal const string Name = "Mix64";

    private const int WarmUpBlocks = 200;
    private const int MeasuredBlocks = 2000;
    private const int ClipRate = 32_000;
    private const int StreamRate = 44_100;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

    internal static WorkloadRecord Run()
    {
        AudioRing<MixCommand> commands = new(AudioPlayer.CommandCapacity);
        AudioRing<VoiceEnded> ended = new(AudioPlayer.EndedCapacity);
        VoiceMixer mixer = new(commands, ended);
        using AudioStreamer streamer = new();

        Random random = new(64);
        PcmAudio[] clips = new PcmAudio[4];
        for (int i = 0; i < clips.Length; i++)
        {
            short[] samples = new short[2 * ClipRate * (i + 1)];
            for (int sample = 0; sample < samples.Length; sample++)
            {
                samples[sample] = (short)random.Next(short.MinValue, short.MaxValue);
            }

            clips[i] = new PcmAudio(samples, 2, ClipRate);
        }

        for (int slot = 0; slot < AudioMixer.MaxVoices - 1; slot++)
        {
            PcmAudio clip = clips[slot % clips.Length];
            float pitch = 0.5f + (1.5f * slot / (AudioMixer.MaxVoices - 1));
            commands.TryWrite(new MixCommand(MixCommandKind.Play, slot, 1, 0.1f, pitch, (slot % 5 * 0.5f) - 1f)
            {
                Samples = clip,
                Loop = true,
                LoopStart = clip.Frames / 4,
                LoopEnd = clip.Frames,
            });
        }

        float[] music = new float[2 * StreamRate * 4];
        for (int sample = 0; sample < music.Length; sample++)
        {
            music[sample] = (float)((random.NextDouble() * 2.0) - 1.0);
        }

        AudioStream stream = streamer.Play(new MemorySource(music), new AudioClip("music", ".ogg", 4.0), loop: true);
        commands.TryWrite(new MixCommand(MixCommandKind.Play, AudioMixer.MaxVoices - 1, 1, 0.5f) { Stream = stream });

        // Blocks run back to back here, far faster than they play. Before each one, untimed, the worker
        // is woken and given the time a run's pacing would give it, which keeps the stream on the
        // spline rather than starved.
        Func<bool> decoded = () => stream.Ended || stream.WrittenSamples - stream.ReadPosition >= AudioStream.Capacity / 2;

        float[] block = new float[VoiceMixer.BlockFrames * VoiceMixer.Channels];
        for (int i = 0; i < WarmUpBlocks; i++)
        {
            Time(block, mixer, streamer, decoded);
        }

        double[] blockMs = new double[MeasuredBlocks];
        int gen0 = GC.CollectionCount(0);
        long underruns = mixer.Underruns;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < MeasuredBlocks; i++)
        {
            blockMs[i] = Time(block, mixer, streamer, decoded);
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (mixer.Underruns != underruns)
        {
            Console.WriteLine($"bench: {Name} starved its stream in {mixer.Underruns - underruns} blocks");
        }

        Percentiles timing = Percentiles.Of(blockMs);

        return new WorkloadRecord(Name, "mixer", "none", MeasuredBlocks, null, null, null, new StepTiming(timing.Median, timing.P95), null, bytes, null, null, null, GC.CollectionCount(0) - gen0, null);
    }

    private static double Time(float[] block, VoiceMixer mixer, AudioStreamer streamer, Func<bool> decoded)
    {
        streamer.Wake();
        SpinWait.SpinUntil(decoded, Patience);

        long start = Stopwatch.GetTimestamp();
        mixer.Mix(block);

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    // Samples already in memory, decoded by copying, as a stand-in for a file on the worker.
    private sealed class MemorySource(float[] samples) : IPcmSource
    {
        private int _offset;

        public int Channels => 2;

        public int SampleRate => StreamRate;

        public void SeekTo(long frame) => _offset = (int)Math.Clamp(frame * 2, 0, samples.Length);

        public int Read(Span<float> target)
        {
            int read = Math.Min(target.Length, samples.Length - _offset);
            samples.AsSpan(_offset, read).CopyTo(target);
            _offset += read;

            return read;
        }

        public void Dispose()
        {
        }
    }
}

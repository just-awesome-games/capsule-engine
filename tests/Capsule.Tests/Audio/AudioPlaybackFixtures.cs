using System.Numerics;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Scenes;

namespace Capsule.Tests.Audio;

internal static class AudioPlaybackFixtures
{
    // The rate every resident clip the fixture loads is decoded at.
    internal const int ResidentRate = 8000;

    internal static readonly AudioClip Step = new("step", ".wav", 0.5);

    internal static readonly AudioClip Music = new("music", ".ogg", 90.0);

    internal static readonly AudioClip Theme = new("theme", ".wav", 8.0, new AudioLoopRegion(2.0, 8.0));

    internal static Voice Slot(int slot, int generation) => Voice.Of(slot, generation);

    internal static AudioCommand Command(
        AudioCommandKind kind,
        Voice voice,
        AudioClip clip = default,
        float gain = 1f,
        float pitch = 1f,
        float pan = 0f,
        bool loop = false,
        double startSeconds = 0.0,
        AudioBus bus = default) => new(kind, voice, clip, bus, gain, pitch, pan, loop, startSeconds);

    // A mixer and the two rings it talks through, with nothing on the other end. A test posts what the
    // game thread would and reads what the mixer reports.
    internal static (VoiceMixer Mixer, AudioRing<MixCommand> Commands, AudioRing<VoiceEnded> Ended) Mixer()
    {
        AudioRing<MixCommand> commands = new(AudioPlayer.CommandCapacity);
        AudioRing<VoiceEnded> ended = new(AudioPlayer.EndedCapacity);

        return (new VoiceMixer(commands, ended), commands, ended);
    }

    // A resident play of samples on slot 0 at generation 1, looping over the whole clip unless a region
    // is named.
    internal static MixCommand Play(PcmAudio samples, float pitch = 1f, bool loop = false, long loopStart = 0, long loopEnd = -1, long startFrame = 0, int bus = 0, int slot = 0, float gain = 1f) =>
        new(MixCommandKind.Play, slot, 1, gain, pitch)
        {
            Samples = samples,
            Loop = loop,
            LoopStart = loopStart,
            LoopEnd = loopEnd < 0 ? samples.Frames : loopEnd,
            StartFrame = startFrame,
            Bus = bus,
        };

    // Interleaved 16-bit samples no two neighbours of which repeat, from seed.
    internal static short[] Noise(int samples, int seed)
    {
        Random random = new(seed);
        short[] noise = new short[samples];
        for (int i = 0; i < noise.Length; i++)
        {
            noise[i] = (short)random.Next(short.MinValue, short.MaxValue);
        }

        return noise;
    }

    internal sealed class StartupScene : Scene
    {
        protected override void OnStart()
        {
            Speaker speaker = new();
            speaker.Add(new AudioSource(Step) { AutoPlay = true, Loop = true });
            Add(speaker);
        }
    }

    internal sealed class Speaker() : Entity(Vector2.Zero);

    // The game thread's side with no mixer behind it. What the player posts is read off its ring, and
    // a test reports a voice ended as the mix thread would.
    internal sealed class Fixture : IDisposable
    {
        private readonly List<MixCommand> _posted = [];

        // The resident clips decoded and the streamed clips opened, in order.
        internal List<AudioClip> Loaded { get; } = [];

        internal List<AudioClip> Opened { get; } = [];

        internal AudioStreamer Streamer { get; } = new();

        internal AudioRing<MixCommand> Commands { get; } = new(AudioPlayer.CommandCapacity);

        internal AudioRing<VoiceEnded> Ended { get; } = new(AudioPlayer.EndedCapacity);

        internal SoundStore Store { get; }

        internal AudioPlayer Player { get; }

        internal Fixture()
        {
            Store = new SoundStore(Read, Open);
            Player = new AudioPlayer(Store, Streamer, Commands, Ended);
        }

        // Every command the player has posted so far, in order.
        internal List<MixCommand> Posted
        {
            get
            {
                while (Commands.TryRead(out MixCommand command))
                {
                    _posted.Add(command);
                }

                return _posted;
            }
        }

        internal void Preload(params AudioClip[] clips)
        {
            AssetCollection preloads = new();
            foreach (AudioClip clip in clips)
            {
                preloads.Add(clip);
            }

            Store.ChangeScene(preloads);
        }

        internal void Apply(params AudioCommand[] commands) => Player.Apply(commands);

        // Reports the voice ended as the mix thread does, and lets the player retire it.
        internal void End(in MixCommand play)
        {
            Assert.True(Ended.TryWrite(new VoiceEnded(play.Slot, play.Generation, play.Stream)));
            Player.Update();
        }

        public void Dispose()
        {
            Store.Dispose();
            Streamer.Dispose();
        }

        // A second of silence at ResidentRate, mono. Called on the thread pool for a preload.
        private PcmAudio Read(AudioClip clip)
        {
            lock (Loaded)
            {
                Loaded.Add(clip);
            }

            return new PcmAudio(new short[(int)(clip.DurationSeconds * ResidentRate)], 1, ResidentRate);
        }

        private SampleSource Open(AudioClip clip)
        {
            Opened.Add(clip);

            return new SampleSource(new float[ResidentRate], 1, ResidentRate);
        }
    }

    // A decoder over samples already in memory, interleaved at channels, for the streamed path with no
    // file. It can be read again after a seek, which lets one source serve every replay.
    internal sealed class SampleSource(float[] samples, int channels, int sampleRate) : IPcmSource
    {
        private int _offset;

        public int Channels => channels;

        public int SampleRate => sampleRate;

        public void SeekTo(long frame) => _offset = (int)Math.Clamp(frame * channels, 0, samples.Length);

        public int Read(Span<float> target)
        {
            int read = Math.Min(target.Length, samples.Length - _offset);
            samples.AsSpan(_offset, read).CopyTo(target);
            _offset += read;

            return read;
        }

        public void Dispose() => _offset = 0;
    }
}

using System.Numerics;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Scenes;

namespace Capsule.Tests.Audio;

internal static class AudioPlaybackFixtures
{
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
        double startSeconds = 0.0) => new(kind, voice, clip, AudioBus.Master, gain, pitch, pan, loop, startSeconds);

    internal sealed class StartupScene : Scene
    {
        protected override void OnStart()
        {
            Speaker speaker = new();
            speaker.Add(new AudioSource(Step) { PlayOnStart = true, Loop = true });
            Add(speaker);
        }
    }

    internal sealed class Speaker() : Entity(Vector2.Zero);

    internal sealed class Fixture : IDisposable
    {
        internal Fixture()
        {
            Store = new SoundStore(Backend);
            Player = new AudioPlayer(Store);
        }

        internal FakeBackend Backend { get; } = new();

        internal SoundStore Store { get; }

        internal AudioPlayer Player { get; }

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

        public void Dispose()
        {
            Player.Dispose();
            Store.Dispose();
        }
    }

    internal sealed class FakeBackend : IAudioBackend
    {
        internal List<FakeSound> Loaded { get; } = [];

        internal List<FakeVoice> Voices { get; } = [];

        public MemoryStream Read(in AudioClip clip) => new();

        public IResidentSound Load(in AudioClip clip, MemoryStream file)
        {
            FakeSound sound = new(this, clip);
            Loaded.Add(sound);

            return sound;
        }

        public IAudioVoice Stream(in AudioClip clip, float gain, float pitch, float pan, bool loop, double startSeconds) =>
            Started(new FakeVoice(clip, gain, pitch, loop) { Pan = pan, StartSeconds = startSeconds });

        public void Dispose()
        {
        }

        internal FakeVoice Started(FakeVoice voice)
        {
            Voices.Add(voice);

            return voice;
        }
    }

    // Pools its voice as the device backend does, so what the store counts is exercised against the
    // one object a repeated play reuses rather than a fresh one per play.
    internal sealed class FakeSound(FakeBackend backend, AudioClip clip) : IResidentSound
    {
        private readonly Stack<FakeVoice> _idle = new();

        internal bool Disposed { get; private set; }

        public IAudioVoice Play(float gain, float pitch, float pan, bool loop, Action ended)
        {
            FakeVoice voice = _idle.Count > 0 ? _idle.Pop() : backend.Started(new FakeVoice(clip));
            voice.Start(gain, pitch, pan, loop, ended, _idle);

            return voice;
        }

        // Never pooled: streaming this sound's samples is a voice of its own.
        public IAudioVoice Stream(float gain, float pitch, float pan, bool loop, double startSeconds, Action ended)
        {
            FakeVoice voice = backend.Started(
                new FakeVoice(clip, gain, pitch, loop) { Pan = pan, StartSeconds = startSeconds, Streamed = true });
            voice.Ends(ended);

            return voice;
        }

        public void Dispose() => Disposed = true;
    }

    internal sealed class FakeVoice(AudioClip clip, float gain = 1f, float pitch = 1f, bool loop = false) : IAudioVoice
    {
        // Set for a resident voice, which is pooled and told to report its retirement; a streamed
        // one is neither.
        private Action? _ended;
        private Stack<FakeVoice>? _pool;

        internal AudioClip Clip => clip;

        // Whether this voice streams the sound's samples rather than letting the device queue it whole.
        internal bool Streamed { get; init; }

        internal double StartSeconds { get; init; }

        internal bool Loop { get; private set; } = loop;

        internal float Gain { get; private set; } = gain;

        internal float Pitch { get; private set; } = pitch;

        internal float Pan { get; set; }

        internal bool Paused { get; private set; }

        internal int Resumes { get; private set; }

        // How many times the pool has handed this voice out; 0 for a streamed one, which is not pooled.
        internal int Plays { get; private set; }

        internal bool Disposed { get; private set; }

        public bool Finished { get; set; }

        public void SetGain(float value) => Gain = value;

        public void SetPitch(float value) => Pitch = value;

        public void SetPan(float value) => Pan = value;

        public void Pause() => Paused = true;

        public void Resume()
        {
            Paused = false;
            Resumes++;
        }

        public void Update()
        {
        }

        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            Disposed = true;

            Action? ended = _ended;
            _ended = null;
            _pool?.Push(this);
            ended?.Invoke();
        }

        internal void Ends(Action ended) => _ended = ended;

        internal void Start(float gain, float pitch, float pan, bool loop, Action ended, Stack<FakeVoice> pool)
        {
            Gain = gain;
            Pitch = pitch;
            Pan = pan;
            Loop = loop;
            Paused = false;
            Finished = false;
            Disposed = false;
            Plays++;
            _ended = ended;
            _pool = pool;
        }
    }
}

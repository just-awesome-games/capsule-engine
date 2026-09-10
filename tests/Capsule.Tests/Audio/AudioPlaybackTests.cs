using System.Numerics;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Audio;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Audio;

// The host's half of the mixer contract: how a step's commands land on a voice table, and how long a
// sound outlives the scene that loaded it. Decoding and the device itself are the backend's, which
// is faked here — nothing below asserts what OpenAL does.
public sealed class AudioPlaybackTests
{
    private static readonly AudioClip Step = new("step", ".wav", 0.5);

    private static readonly AudioClip Music = new("music", ".ogg", 90.0);

    private static readonly AudioClip Theme = new("theme", ".wav", 8.0, new AudioLoopRegion(2.0, 8.0));

    [Fact]
    public void Play_StartsOneVoiceCarryingTheCommandsGainPitchAndLoop()
    {
        using Fixture fixture = new();

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step, gain: 0.25f, pitch: 1.5f, loop: true));

        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(Step, voice.Clip);
        Assert.Equal(0.25f, voice.Gain);
        Assert.Equal(1.5f, voice.Pitch);
        Assert.True(voice.Loop);
    }

    // Every kind other than Play is a settled value for a voice already on the table.
    [Fact]
    public void ACommandReachesTheVoiceItsGenerationNames()
    {
        using Fixture fixture = new();
        Voice voice = Slot(3, 1);

        fixture.Apply(
            Command(AudioCommandKind.Play, voice, Step),
            Command(AudioCommandKind.SetGain, voice, gain: 0.5f),
            Command(AudioCommandKind.SetPitch, voice, pitch: 2f),
            Command(AudioCommandKind.Pause, voice),
            Command(AudioCommandKind.Resume, voice));

        FakeVoice played = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(0.5f, played.Gain);
        Assert.Equal(2f, played.Pitch);
        Assert.False(played.Paused);
        Assert.Equal(1, played.Resumes);
    }

    // A handle whose generation has moved on addresses a voice that was stolen, stopped or expired.
    [Fact]
    public void ACommandForAGenerationTheTableNoLongerHoldsIsDropped()
    {
        using Fixture fixture = new();

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step, gain: 1f));
        fixture.Apply(Command(AudioCommandKind.SetGain, Slot(0, 2), gain: 0f));

        Assert.Equal(1f, Assert.Single(fixture.Backend.Voices).Gain);
    }

    // The mixer raises the stolen voice's Stop ahead of the new voice's Play, both on one slot. The
    // sound pools its voice, so the one the steal ended is the one the new play sounds on.
    [Fact]
    public void AStolenSlotEndsItsOldVoiceBeforeTheNewOneStarts()
    {
        using Fixture fixture = new();

        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step, gain: 0.25f));
        fixture.Apply(
            Command(AudioCommandKind.Stop, Slot(0, 1)),
            Command(AudioCommandKind.Play, Slot(0, 2), Step, gain: 1f));

        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(2, voice.Plays);
        Assert.False(voice.Disposed);
        Assert.Equal(1f, voice.Gain);
    }

    // The retirement that pooling reports is what the residency count is kept on, so a sound whose
    // voice has been played, retired and played again is still held by exactly one live voice.
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

    // An expired one-shot raises no Stop at all, so the table retires it on what the device says.
    [Fact]
    public void AVoiceTheDeviceFinishedIsRetiredWithoutACommand()
    {
        using Fixture fixture = new();
        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step));

        FakeVoice voice = Assert.Single(fixture.Backend.Voices);
        voice.Finished = true;
        fixture.Player.Update();

        Assert.True(voice.Disposed);
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

    [Fact]
    public void Play_CarriesTheCommandsPan_AndSetPanMovesTheVoiceAfterwards()
    {
        using Fixture fixture = new();
        Voice voice = Slot(0, 1);

        fixture.Apply(Command(AudioCommandKind.Play, voice, Step, pan: -1f));

        FakeVoice played = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(-1f, played.Pan);

        fixture.Apply(Command(AudioCommandKind.SetPan, voice, pan: 0.5f));

        Assert.Equal(0.5f, played.Pan);
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

        using SceneHost host = new(SceneTransition.ToScene(typeof(StartupScene), null), (in SceneTransition _) => scene);
        using Fixture fixture = new();

        // The order LoadContent boots in: the scene's preloads, then whatever its start left standing.
        fixture.Preload(Step);
        fixture.Player.Apply(host.Audio.Commands);

        FakeVoice sounding = Assert.Single(fixture.Backend.Voices);
        Assert.Equal(Step, sounding.Clip);
        Assert.True(sounding.Loop);

        // Nothing carries it past here, which is what makes the boot delivery the only chance at it.
        host.Step(SceneFixtures.Step(0));

        Assert.Empty(host.Audio.Commands.ToArray());
    }

    private static Voice Slot(int slot, int generation) => Voice.Of(slot, generation);

    private static AudioCommand Command(
        AudioCommandKind kind,
        Voice voice,
        AudioClip clip = default,
        float gain = 1f,
        float pitch = 1f,
        float pan = 0f,
        bool loop = false,
        double startSeconds = 0.0) => new(kind, voice, clip, AudioBus.Master, gain, pitch, pan, loop, startSeconds);

    private sealed class StartupScene : Scene
    {
        protected override void OnStart()
        {
            Speaker speaker = new();
            speaker.Add(new AudioSource(Step) { PlayOnStart = true, Loop = true });
            Add(speaker);
        }
    }

    private sealed class Speaker() : Entity(Vector2.Zero);

    private sealed class Fixture : IDisposable
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

    private sealed class FakeBackend : IAudioBackend
    {
        internal List<FakeSound> Loaded { get; } = [];

        internal List<FakeVoice> Voices { get; } = [];

        public IResidentSound Load(in AudioClip clip)
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
    private sealed class FakeSound(FakeBackend backend, AudioClip clip) : IResidentSound
    {
        private readonly Stack<FakeVoice> _idle = new();

        internal bool Disposed { get; private set; }

        public IAudioVoice Play(float gain, float pitch, float pan, bool loop, Action retired)
        {
            FakeVoice voice = _idle.Count > 0 ? _idle.Pop() : backend.Started(new FakeVoice(clip));
            voice.Start(gain, pitch, pan, loop, retired, _idle);

            return voice;
        }

        // Never pooled: streaming this sound's samples is a voice of its own.
        public IAudioVoice Stream(float gain, float pitch, float pan, bool loop, double startSeconds, Action retired)
        {
            FakeVoice voice = backend.Started(
                new FakeVoice(clip, gain, pitch, loop) { Pan = pan, StartSeconds = startSeconds, Streamed = true });
            voice.Retires(retired);

            return voice;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeVoice(AudioClip clip, float gain = 1f, float pitch = 1f, bool loop = false) : IAudioVoice
    {
        // Set for a resident voice, which is pooled and told to report its retirement; a streamed
        // one is neither.
        private Action? _retired;
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

            Action? retired = _retired;
            _retired = null;
            _pool?.Push(this);
            retired?.Invoke();
        }

        internal void Retires(Action retired) => _retired = retired;

        internal void Start(float gain, float pitch, float pan, bool loop, Action retired, Stack<FakeVoice> pool)
        {
            Gain = gain;
            Pitch = pitch;
            Pan = pan;
            Loop = loop;
            Paused = false;
            Finished = false;
            Disposed = false;
            Plays++;
            _retired = retired;
            _pool = pool;
        }
    }
}

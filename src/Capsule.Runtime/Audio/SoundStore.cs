using Capsule.Assets;
using Capsule.Audio;
using Capsule.Runtime.Assets;

namespace Capsule.Runtime.Audio;

// The sounds owned by the current scene, preloaded where declared and otherwise loaded on first
// play. Streamed clips are never resident: they are opened per voice and released at retire.
//
// A voice outlives the scene that started it — the mixer is the run's, not the scene's — so a
// resident sound the outgoing scene drops is retained until its last live voice retires, and only
// then released.
internal sealed class SoundStore : IDisposable
{
    private readonly IAudioBackend _backend;
    private readonly SceneAssetStore<AudioClip, RetainedSound> _resident;

    internal SoundStore(IAudioBackend backend)
    {
        _backend = backend;
        _resident = new(clip => new RetainedSound(backend.Load(clip)));
    }

    // Missing preloads are decoded before the prior scene's sounds are released. Streamed clips are
    // dropped here rather than loaded; declaring one is not an error, it simply reserves nothing.
    internal void ChangeScene(AssetCollection preloads)
    {
        List<AudioClip> resident = [];
        foreach (AudioClip clip in preloads.Clips)
        {
            if (!AudioFiles.IsStreamed(clip))
            {
                resident.Add(clip);
            }
        }

        _resident.ChangeScene(resident);
    }

    // Loads on first use when the scene did not preload the clip. A resident clip streams its own
    // samples instead of being queued whole when it must repeat a loop region or begin mid-clip: the
    // device can only repeat all of what it was queued, and only from the front of it.
    internal IAudioVoice Play(in AudioClip clip, float gain, float pitch, float pan, bool loop, double startSeconds) =>
        AudioFiles.IsStreamed(clip)
            ? _backend.Stream(clip, gain, pitch, pan, loop, startSeconds)
            : _resident.Get(clip).Play(
                gain,
                pitch,
                pan,
                loop,
                startSeconds,
                startSeconds > 0.0 || (loop && clip.LoopRegion.HasRegion));

    public void Dispose()
    {
        try
        {
            _resident.Dispose();
        }
        finally
        {
            _backend.Dispose();
        }
    }

    // One resident sound plus the count of live voices playing it. The scene's release and the last
    // voice's retire race in either order; whichever is last frees the sound.
    private sealed class RetainedSound : IDisposable
    {
        private readonly IResidentSound _sound;

        // One delegate for every voice this sound ever plays, so a play allocates nothing.
        private readonly Action _retire;

        private int _voices;
        private bool _released;

        internal RetainedSound(IResidentSound sound)
        {
            _sound = sound;
            _retire = Retire;
        }

        internal IAudioVoice Play(float gain, float pitch, float pan, bool loop, double startSeconds, bool stream)
        {
            IAudioVoice voice = stream
                ? _sound.Stream(gain, pitch, pan, loop, startSeconds, _retire)
                : _sound.Play(gain, pitch, pan, loop, _retire);
            _voices++;

            return voice;
        }

        // Called when the scene that owned this sound no longer wants it.
        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            Free();
        }

        private void Retire()
        {
            _voices--;
            Free();
        }

        private void Free()
        {
            if (_released && _voices == 0)
            {
                _sound.Dispose();
            }
        }
    }
}

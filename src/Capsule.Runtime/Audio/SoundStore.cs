using Capsule.Assets;
using Capsule.Audio;
using Capsule.Runtime.Assets;

namespace Capsule.Runtime.Audio;

// The sounds owned by the current scene, preloaded where declared and otherwise loaded on first play.
// Streamed clips are not resident. They are opened per voice and closed when the voice ends.
//
// A voice outlives the scene that started it, because the mixer is the run's and not the scene's, so
// a resident sound the outgoing scene drops is held until its last live voice ends.
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
    // dropped here instead of loaded. Declaring one is not an error and reserves nothing.
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
    // samples instead of being queued whole when it must repeat a loop region or begin mid-clip,
    // because the device repeats all of what it was queued and only from the front.
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
    // voice's end race in either order, and whichever is last frees the sound.
    private sealed class RetainedSound : IDisposable
    {
        private readonly IResidentSound _sound;

        // One delegate shared by every voice this sound plays, which keeps a play allocation-free.
        private readonly Action _ended;

        private int _voices;
        private bool _released;

        internal RetainedSound(IResidentSound sound)
        {
            _sound = sound;
            _ended = Ended;
        }

        internal IAudioVoice Play(float gain, float pitch, float pan, bool loop, double startSeconds, bool stream)
        {
            IAudioVoice voice = stream
                ? _sound.Stream(gain, pitch, pan, loop, startSeconds, _ended)
                : _sound.Play(gain, pitch, pan, loop, _ended);
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

        private void Ended()
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

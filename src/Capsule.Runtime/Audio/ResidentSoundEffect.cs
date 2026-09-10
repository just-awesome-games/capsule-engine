using Capsule.Audio;
using Microsoft.Xna.Framework.Audio;

namespace Capsule.Runtime.Audio;

// One decoded sound plus the voices played from it. A voice is the OpenAL source and the handle the
// player holds, pooled as one object: a clip that is only ever heard one at a time allocates exactly
// one for the life of the scene, a second appears only when a second voice wants it at the same
// moment, and a play the pool can serve allocates nothing.
internal sealed class ResidentSoundEffect(SoundEffect effect, AudioClip clip, AudioStreamer streamer) : IResidentSound
{
    private readonly Stack<PooledVoice> _idle = new();

    // Read on the first play the device cannot queue whole and kept for as long as the sound is:
    // beside the SoundEffect rather than instead of it, so a plain one-shot of the same clip is still
    // queued whole and a clip only ever played that way never pays the samples' memory. Every later
    // streamed play of it reads no file.
    private PcmAudio? _samples;

    public IAudioVoice Play(float gain, float pitch, float pan, bool loop, Action retired)
    {
        PooledVoice voice = _idle.Count > 0 ? _idle.Pop() : new PooledVoice(this, effect.CreateInstance());
        voice.Start(gain, pitch, pan, loop, retired);

        return voice;
    }

    // The streamed voice is the streamer's to pool, not this sound's: its queue, its buffers, its
    // loop reader and its cursor over these samples are fixed by the clip's rate and channel count
    // rather than by the clip, so every replay of a region loop or an offset start — of this clip or
    // any other of that format — reuses them and allocates nothing.
    public IAudioVoice Stream(float gain, float pitch, float pan, bool loop, double startSeconds, Action retired)
    {
        _samples ??= PcmAudio.FromWav(AudioFiles.Locate(AppContext.BaseDirectory, clip), clip.Name);

        return streamer.Play(_samples, clip, gain, pitch, pan, loop, startSeconds, retired);
    }

    public void Dispose()
    {
        // Reached only once no voice holds an instance, so the pool is whole by here.
        while (_idle.Count > 0)
        {
            _idle.Pop().Release();
        }

        effect.Dispose();
    }

    private void Return(PooledVoice voice) => _idle.Push(voice);

    private sealed class PooledVoice(ResidentSoundEffect owner, SoundEffectInstance instance) : IAudioVoice
    {
        // Non-null exactly while this voice is out on a play, which is what makes retiring idempotent.
        private Action? _retired;

        public bool Finished => instance.State == SoundState.Stopped;

        public void SetGain(float gain) => instance.Volume = gain;

        public void SetPitch(float pitch) => instance.Pitch = SoundPitch.Octaves(pitch);

        public void SetPan(float pan) => instance.Pan = pan;

        public void Pause() => instance.Pause();

        public void Resume() => instance.Resume();

        public void Update()
        {
            // A resident sound is queued whole; nothing is handed to the device per frame.
        }

        public void Dispose()
        {
            if (_retired is not { } retired)
            {
                return;
            }

            _retired = null;
            instance.Stop();

            // Pooled before the retirement is reported: reporting it can release the sound, and the
            // release frees the instances the pool holds.
            owner.Return(this);
            retired();
        }

        internal void Start(float gain, float pitch, float pan, bool loop, Action retired)
        {
            _retired = retired;

            // Every property is written on every play: a pooled instance carries the last voice's.
            instance.Volume = gain;
            instance.Pitch = SoundPitch.Octaves(pitch);
            instance.Pan = pan;
            instance.IsLooped = loop;
            instance.Play();
        }

        // Ends the pooled source itself, once the sound it belongs to is going away.
        internal void Release() => instance.Dispose();
    }
}

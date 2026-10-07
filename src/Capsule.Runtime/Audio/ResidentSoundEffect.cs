using Capsule.Audio;
using Microsoft.Xna.Framework.Audio;

namespace Capsule.Runtime.Audio;

// One decoded sound plus the voices played from it. A voice is the OpenAL source and the handle the
// player holds, pooled as one object. A clip heard one at a time allocates a single voice for the life
// of the scene, a second appears when a second voice wants it at the same moment, and a play the pool
// can serve allocates nothing.
internal sealed class ResidentSoundEffect(
    SoundEffect effect,
    AudioClip clip,
    PcmAudio? samples,
    AudioStreamer streamer,
    HostPlatform platform) : IResidentSound
{
    private readonly Stack<PooledVoice> _pooled = new();

    // The decoded clip, for plays the device cannot queue whole. A clip with a loop region arrives
    // decoded, and any other decodes on its first such play. It sits beside the SoundEffect, so a plain
    // one-shot is still queued whole. A clip without a region that only plays that way never pays the
    // samples' memory.
    private PcmAudio? _samples = samples;

    public IAudioVoice Play(float gain, float pitch, float pan, bool loop, Action ended)
    {
        PooledVoice voice = _pooled.Count > 0 ? _pooled.Pop() : new PooledVoice(this, effect.CreateInstance());
        voice.Start(gain, pitch, pan, loop, ended);

        return voice;
    }

    // The streamer pools the streamed voice, not this sound. Its queue, its buffers, its loop reader
    // and its cursor over these samples are fixed by the clip's rate and channel count, so every
    // replay of a region loop or an offset start reuses them, whether it is this clip or any other of
    // that format.
    public IAudioVoice Stream(float gain, float pitch, float pan, bool loop, double startSeconds, Action ended)
    {
        _samples ??= PcmAudio.FromWav(AudioFiles.Open(platform, clip), clip.Name);

        return streamer.Play(_samples, clip, gain, pitch, pan, loop, startSeconds, ended);
    }

    public void Dispose()
    {
        // Reached once no voice holds an instance, so the pool is complete by here.
        while (_pooled.Count > 0)
        {
            _pooled.Pop().Close();
        }

        effect.Dispose();
    }

    private void Return(PooledVoice voice) => _pooled.Push(voice);

    private sealed class PooledVoice(ResidentSoundEffect owner, SoundEffectInstance instance) : IAudioVoice
    {
        // Non-null while this voice is out on a play, which makes retiring idempotent.
        private Action? _ended;

        public bool Finished => instance.State == SoundState.Stopped;

        public void SetGain(float gain) => instance.Volume = gain;

        public void SetPitch(float pitch) => instance.Pitch = SoundPitch.Octaves(pitch);

        public void SetPan(float pan) => instance.Pan = pan;

        public void Pause() => instance.Pause();

        public void Resume() => instance.Resume();

        public void Update()
        {
        }

        public void Dispose()
        {
            if (_ended is not { } ended)
            {
                return;
            }

            _ended = null;
            instance.Stop();

            // Pooled before the end is reported, because reporting it can release the sound and that
            // release frees the instances the pool holds.
            owner.Return(this);
            ended();
        }

        internal void Start(float gain, float pitch, float pan, bool loop, Action ended)
        {
            _ended = ended;

            // Every property is written on every play, since a pooled instance carries the last
            // voice's values.
            instance.Volume = gain;
            instance.Pitch = SoundPitch.Octaves(pitch);
            instance.Pan = pan;
            instance.IsLooped = loop;
            instance.Play();
        }

        // Ends the pooled source, once the sound it belongs to is going away.
        internal void Close() => instance.Dispose();
    }
}

using Microsoft.Xna.Framework.Audio;

namespace Capsule.Runtime.Audio;

// The device's own queue: one MonoGame dynamic instance, which is an OpenAL streaming source. Its
// format is fixed at construction, so a queue is only ever reused for a clip of the same rate and
// channel count.
internal sealed class DynamicPcmQueue : IPcmQueue
{
    private readonly DynamicSoundEffectInstance _instance;

    internal DynamicPcmQueue(int sampleRate, int channels) =>
        _instance = new DynamicSoundEffectInstance(
            sampleRate,
            channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);

    public int Pending => _instance.PendingBufferCount;

    public bool Stopped => _instance.State == SoundState.Stopped;

    public void SetGain(float gain) => _instance.Volume = gain;

    public void SetPitch(float pitch) => _instance.Pitch = SoundPitch.Octaves(pitch);

    // A mono source is rotated across the whole field; a stereo one only where the device reports
    // AL_EXT_STEREO_ANGLES, and plays centred where it does not.
    public void SetPan(float pan) => _instance.Pan = pan;

    public void Submit(byte[] buffer, int bytes) => _instance.SubmitBuffer(buffer, 0, bytes);

    public void Play() => _instance.Play();

    public void Pause() => _instance.Pause();

    public void Resume() => _instance.Resume();

    // Immediate rather than at the end of what is queued: the buffers are dropped, which is what
    // leaves the queue clean for the next voice.
    public void Stop() => _instance.Stop(immediate: true);

    public void Dispose() => _instance.Dispose();
}

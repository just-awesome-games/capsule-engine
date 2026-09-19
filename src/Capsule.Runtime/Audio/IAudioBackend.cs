using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// Whatever turns a clip into sound. The store and the player are written against this interface, so
// scene residency, retention and command translation are exercised with no device open.
internal interface IAudioBackend : IDisposable
{
    // The sound for a resident clip, loaded whole. Throws when the shipped file is missing or
    // unreadable, which is a build fault and not a device one.
    IResidentSound Load(in AudioClip clip);

    // A voice decoding its clip as it plays, beginning at startSeconds. Nothing is resident, so the
    // clip is opened per play and closed when the voice ends.
    IAudioVoice Stream(in AudioClip clip, float gain, float pitch, float pan, bool loop, double startSeconds);
}

// One clip held in memory for as long as a scene or a live voice wants it.
internal interface IResidentSound : IDisposable
{
    // The voice belongs to the sound and is pooled across plays, so the caller may be handed the same
    // object again. ended is invoked once the voice has gone back to that pool, which counts residency
    // without a second object per play. The caller should cache it, since it is the same for every
    // voice this sound plays.
    IAudioVoice Play(float gain, float pitch, float pan, bool loop, Action ended);

    // This sound's samples streamed instead of queued whole, from startSeconds on. The device queues a
    // clip whole, so it can neither repeat part of one nor begin mid-buffer. This is the path for a
    // voice repeating a loop region and for one played from an offset. ended is reported as Play
    // reports it.
    IAudioVoice Stream(float gain, float pitch, float pan, bool loop, double startSeconds, Action ended);
}

// One sounding voice. Disposing it ends the sound and returns whatever the backend pooled for it.
internal interface IAudioVoice : IDisposable
{
    // Whether the sound has run out on its own. A loop never does.
    bool Finished { get; }

    // Linear amplitude in [0, 1], already resolved against the master and bus volumes.
    void SetGain(float gain);

    // Playback-rate multiplier, positive and finite.
    void SetPitch(float pitch);

    // Stereo position in [-1, 1], -1 hard left.
    void SetPan(float pan);

    void Pause();

    void Resume();

    // Called from the game thread once a frame, including a frame that runs no step. A voice with
    // nothing to hand the device does nothing here.
    void Update();
}

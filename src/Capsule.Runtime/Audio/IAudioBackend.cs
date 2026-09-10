using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// Whatever turns a clip into sound. The store and the player are written against this alone, so
// scene residency, retention and command translation are exercised with no device open.
internal interface IAudioBackend : IDisposable
{
    // The sound for a resident clip, loaded whole. Throws when the shipped file is missing or
    // unreadable, which is a build fault rather than a device one.
    IResidentSound Load(in AudioClip clip);

    // A voice decoding its clip as it plays, beginning at startSeconds. Nothing about it is resident,
    // so it is opened per play and released at retire.
    IAudioVoice Stream(in AudioClip clip, float gain, float pitch, float pan, bool loop, double startSeconds);
}

// One clip held in memory for as long as a scene or a live voice wants it.
internal interface IResidentSound : IDisposable
{
    // The voice is the sound's own and is pooled across plays, so the caller is handed the same
    // object again rather than a fresh one. retired is invoked once the voice has gone back to that
    // pool, which is how residency is counted without a second object per play; it is the caller's
    // to cache, since it is the same for every voice this sound plays.
    IAudioVoice Play(float gain, float pitch, float pan, bool loop, Action retired);

    // This sound's samples streamed rather than queued whole, from startSeconds on. The device queues
    // a clip whole, so it can neither repeat part of one nor begin mid-buffer: this is the path for a
    // voice repeating a loop region and for one played from an offset. retired is reported as Play
    // reports it.
    IAudioVoice Stream(float gain, float pitch, float pan, bool loop, double startSeconds, Action retired);
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

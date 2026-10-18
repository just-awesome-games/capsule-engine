namespace Capsule.Runtime.Audio;

// The device queue a streamed voice hands PCM to: an OpenAL streaming source in a running game, and a
// counter in the suite. The decode, the loop wrap and the voice pool are exercised with no device
// open. Submitting copies. A buffer is the voice's again the moment it returns.
//
// A queue outlives the voices played on it. It is pooled with the voice, and a stop leaves it
// playable.
internal interface IPcmQueue : IDisposable
{
    // Buffers submitted and not yet played out.
    int Pending { get; }

    // Whether the device is silent, which a voice starved of buffers plays out of.
    bool Stopped { get; }

    void SetGain(float gain);

    void SetPitch(float pitch);

    void SetPan(float pan);

    void Submit(byte[] buffer, int bytes);

    void Play();

    void Pause();

    void Resume();

    // Ends the sound and drops everything queued. A queue handed to the next voice carries nothing
    // of the last one.
    void Stop();
}

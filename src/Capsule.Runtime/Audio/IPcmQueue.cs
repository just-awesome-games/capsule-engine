namespace Capsule.Runtime.Audio;

// The device queue a streamed voice hands PCM to: an OpenAL streaming source in a running game, and
// a counter in the suite, so the decode, the loop wrap and the voice pool are exercised with no
// device open. Submitting copies, so a buffer is the voice's again the moment it returns.
//
// A queue outlives the voices played on it: it is pooled with the voice, and a stop leaves it
// playable rather than spent.
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

    // Ends the sound and drops everything queued: a queue handed to the next voice carries nothing
    // of the last one.
    void Stop();
}

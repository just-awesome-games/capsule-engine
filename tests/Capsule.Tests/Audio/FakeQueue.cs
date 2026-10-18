using Capsule.Runtime.Audio;

namespace Capsule.Tests.Audio;

// A device queue that counts rather than sounds, and remembers every distinct buffer it was
// handed: what the voice reusing its own buffers is read off.
internal sealed class FakeQueue : IPcmQueue
{
    private int _pending;

    internal List<byte[]> Buffers { get; } = [];

    public int Pending => Volatile.Read(ref _pending);

    public bool Stopped { get; private set; } = true;

    public void SetGain(float gain)
    {
    }

    public void SetPitch(float pitch)
    {
    }

    public void SetPan(float pan)
    {
    }

    public void Submit(byte[] buffer, int bytes)
    {
        // Contains rather than a predicate: the game thread submits inside the window a replay's
        // allocations are measured in, and a closure over the buffer would be one of them.
        if (!Buffers.Contains(buffer))
        {
            Buffers.Add(buffer);
        }

        Interlocked.Increment(ref _pending);
    }

    public void Play() => Stopped = false;

    public void Pause() => Stopped = true;

    public void Resume() => Stopped = false;

    public void Stop()
    {
        Stopped = true;
        Volatile.Write(ref _pending, 0);
    }

    public void Dispose() => Stop();

    // One frame of the device consuming what it was queued.
    internal void PlayOut() => Volatile.Write(ref _pending, 0);
}

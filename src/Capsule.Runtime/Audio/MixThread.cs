namespace Capsule.Runtime.Audio;

// The host's mix thread: it mixes one block at a time ahead into the output while the output holds
// less than the lead, and otherwise sleeps until it would fall below it. The output's own device
// thread runs no managed code. A collection pauses only this producer, and the queued lead plays
// through it.
//
// Pushing the mix from a thread is the desktop host's policy. The mixer itself is pure, and a host
// without threads could pump it from its frame loop instead.
internal sealed class MixThread : IDisposable
{
    // About 20 ms queued ahead of the device. Short enough that a command is heard within a frame or
    // two, and long enough to ride out a collection.
    internal const int LeadFrames = VoiceMixer.SampleRate / 50;

    private readonly VoiceMixer _mixer;
    private readonly AudioOutput _output;

    // Mixed into while the output has lost its device, whose queue may never drain again. The voices
    // keep time and the command ring keeps draining until the host reopens the output.
    private readonly SilentOutput _silence = new();

    private readonly Thread _thread;

    // The warning for a mix that stopped, written by this thread and logged by the game thread,
    // because a log sink is written only on the simulation thread.
    private string? _fault;
    private volatile bool _stopping;

    internal MixThread(VoiceMixer mixer, AudioOutput output)
    {
        _mixer = mixer;
        _output = output;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Capsule audio mix",
            Priority = ThreadPriority.AboveNormal,
        };

        _thread.Start();
    }

    // The fault that stopped the mix, once, or null.
    internal string? TakeFault() => Interlocked.Exchange(ref _fault, null);

    // Joins the thread. The output is the caller's to dispose after.
    public void Dispose()
    {
        _stopping = true;
        _thread.Join();
    }

    private void Run()
    {
        float[] block = new float[VoiceMixer.BlockFrames * VoiceMixer.Channels];

        try
        {
            while (!_stopping)
            {
                AudioOutput output = _output.Connected ? _output : _silence;
                int queued = output.QueuedFrames;
                if (queued < LeadFrames)
                {
                    _mixer.Mix(block);
                    output.Submit(block);

                    continue;
                }

                Thread.Sleep(Math.Max(1, (queued - LeadFrames) * 1000 / VoiceMixer.SampleRate));
            }
        }
        catch (Exception failure)
        {
            // This is not the frame's thread. It must not crash, and the game thread logs the fault.
            Volatile.Write(ref _fault, $"audio: the mix stopped and the game falls silent. {failure.Message}");
        }
    }
}

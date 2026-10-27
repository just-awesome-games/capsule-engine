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
    // About 30 ms queued ahead of the device unless the game sets its audio latency. A command is still
    // heard within a frame or two. The device takes about 10 ms from the queue at a time, and the 20 ms
    // left ride out a collection or a sleep that wakes late on a coarse system timer.
    internal const int DefaultLeadFrames = VoiceMixer.SampleRate * 3 / 100;

    private readonly VoiceMixer _mixer;
    private readonly AudioOutput _output;

    // The frames kept queued, and the frames mixed at a time. A chunk is a block, or half a lead shorter
    // than two blocks, and a top-up never queues more than a chunk past the lead.
    private readonly int _leadFrames;
    private readonly int _chunkFrames;

    private readonly Thread _thread;

    // The warning for a mix that stopped, written by this thread and logged by the game thread,
    // because a log sink is written only on the simulation thread.
    private string? _fault;
    private volatile bool _stopping;

    // Times the output was found to have played everything this thread queued.
    private long _underruns;

    internal MixThread(VoiceMixer mixer, AudioOutput output, int leadFrames)
    {
        _mixer = mixer;
        _output = output;
        _leadFrames = leadFrames;
        _chunkFrames = Math.Min(VoiceMixer.BlockFrames, leadFrames / 2);
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Capsule audio mix",
            Priority = ThreadPriority.AboveNormal,
        };

        _thread.Start();
    }

    // The lead a mix into a device taking deviceFrames at a time keeps. A queue shorter than the device's
    // buffer runs dry at every pull, and half a buffer more rides out the mix's own wake.
    internal static int LeadFor(int leadFrames, int deviceFrames) => Math.Max(leadFrames, deviceFrames * 3 / 2);

    internal long Underruns => Volatile.Read(ref _underruns);

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
        float[] block = new float[_chunkFrames * VoiceMixer.Channels];
        bool fed = false;

        try
        {
            _output.RaiseMixThreadPriority();

            while (!_stopping)
            {
                int queued = _output.QueuedFrames;
                if (queued < _leadFrames)
                {
                    // The device has played everything queued: this thread woke a whole lead late, and
                    // a gap is at most one device buffer away.
                    if (fed && queued == 0)
                    {
                        Volatile.Write(ref _underruns, _underruns + 1);
                    }

                    _mixer.Mix(block);
                    _output.Submit(block);
                    fed = true;

                    continue;
                }

                Thread.Sleep(Math.Max(1, (queued - _leadFrames) * 1000 / VoiceMixer.SampleRate));
            }
        }
        catch (Exception failure)
        {
            // This is not the frame's thread. It must not crash, and the game thread logs the fault.
            Volatile.Write(ref _fault, $"audio: the mix stopped and the game falls silent. {failure.Message}");
        }
    }
}

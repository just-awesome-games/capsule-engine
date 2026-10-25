using Capsule.Audio;
using Capsule.Diagnostics;

namespace Capsule.Runtime.Audio;

// The background thread every streamed voice decodes on, and the pool those streams come from.
// Decoding ahead off both the game thread and the mix thread keeps a frame and a mix block from paying
// for a Vorbis packet. A stream that falls behind starves only its own voice, which plays silence.
//
// A stream's source is closed here, not by whoever retired it. Nothing is freed under a decode in
// progress. Closing the source also returns the stream to the pool, and a stream handed out again is
// one no decode still holds. The pool holds no more streams than a run has sounded at once, and a
// replay allocates none of them.
internal sealed class AudioStreamer : IDisposable
{
    // How long an idle worker waits before looking again. A retirement wakes it. A ring holds a third
    // of a second, far beyond this.
    private const int IdleMilliseconds = 5;

    private readonly Lock _gate = new();
    private readonly List<AudioStream> _streams = [];
    private readonly Stack<AudioStream> _pooled = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;

    // The warning for a worker that stopped, written by the worker and logged by the game thread,
    // because a log sink is written only on the simulation thread.
    private string? _fault;
    private volatile bool _stopping;
    private bool _disposed;

    internal AudioStreamer()
    {
        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "Capsule audio streaming",
        };

        _worker.Start();
    }

    // Streams waiting in the pool to be played again.
    internal int Pooled
    {
        get
        {
            lock (_gate)
            {
                return _pooled.Count;
            }
        }
    }

    // Arms a stream over source from startSeconds on, reusing a pooled one. A file handle is not
    // poolable. The caller opens the source per play and the stream owns it from here on.
    internal AudioStream Play(IPcmSource source, in AudioClip clip, bool loop, double startSeconds = 0.0)
    {
        AudioStream? stream = null;
        lock (_gate)
        {
            _pooled.TryPop(out stream);
        }

        stream ??= new AudioStream(this);

        // Rounded to the frame the clip time names, like the loop region's bounds. A file that fails to
        // arm closes, and its stream goes back to the pool unplayed.
        try
        {
            stream.Start(source, clip, loop, (long)Math.Round(startSeconds * source.SampleRate));
        }
        catch
        {
            // Start takes the source first. Closing the stream's source disposes it.
            stream.CloseSource();

            lock (_gate)
            {
                _pooled.Push(stream);
            }

            throw;
        }

        lock (_gate)
        {
            _streams.Add(stream);
        }

        Wake();

        return stream;
    }

    // On the game thread once no mix will read a stream again. Every stream goes back to the pool.
    internal void RetireAll()
    {
        lock (_gate)
        {
            foreach (AudioStream stream in _streams)
            {
                stream.Retire();
            }
        }
    }

    internal void Wake()
    {
        if (!_disposed)
        {
            _wake.Set();
        }
    }

    // Once a frame, on the game thread. Logs a worker fault and each stream's decode fault once.
    internal void Update()
    {
        if (Interlocked.Exchange(ref _fault, null) is { } fault)
        {
            Log.Warning(fault);
        }

        lock (_gate)
        {
            foreach (AudioStream stream in _streams)
            {
                stream.ReportFault();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _stopping = true;
        _wake.Set();
        _worker.Join();
        _disposed = true;

        // The worker is joined. This thread releases what it left and logs any fault a stream still holds.
        foreach (AudioStream stream in _streams)
        {
            stream.ReportFault();
            stream.CloseSource();
        }

        foreach (AudioStream stream in _pooled)
        {
            stream.ReportFault();
        }

        _streams.Clear();
        _pooled.Clear();
        _wake.Dispose();
    }

    private void Work()
    {
        try
        {
            Decode();
        }
        catch (Exception failure)
        {
            // A decode fault silences its own stream inside Fill. Reaching here means the worker is
            // gone and every streamed voice from now on stays silent. This is not the frame's thread.
            // It must not crash, and Update logs the fault on the game thread.
            Volatile.Write(ref _fault, $"audio: the streaming worker stopped and streamed sound falls silent. {failure.Message}");
        }
    }

    private void Decode()
    {
        List<AudioStream> pass = [];

        while (!_stopping)
        {
            pass.Clear();
            lock (_gate)
            {
                pass.AddRange(_streams);
            }

            bool worked = false;

            foreach (AudioStream stream in pass)
            {
                if (stream.Returning)
                {
                    // Released before it is pooled. The game thread takes from the pool, and a stream
                    // reaches it holding no source a decode could still read.
                    stream.CloseSource();

                    lock (_gate)
                    {
                        _streams.Remove(stream);
                        _pooled.Push(stream);
                    }

                    worked = true;

                    continue;
                }

                worked |= stream.Fill();
            }

            if (!worked)
            {
                _wake.WaitOne(IdleMilliseconds);
            }
        }
    }
}

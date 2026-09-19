using Capsule.Audio;
using Capsule.Diagnostics;

namespace Capsule.Runtime.Audio;

// The background thread every streamed voice decodes on, and the pool those voices come from.
// Decoding ahead off the game thread keeps a frame from paying for a Vorbis packet, and a voice that
// falls behind starves its own device queue instead of the frame.
//
// A voice's source is closed here, not by whoever ended it, so nothing is freed under a decode in
// progress. Closing the source also returns the voice to the pool, and a voice handed out again is one
// no decode still holds. The pool is keyed by format, because a voice's queue and buffers are
// sized for one rate and channel count. It holds no more voices than a run has sounded at once, and a
// replay of a format already heard allocates none of them.
internal sealed class AudioStreamer : IDisposable
{
    // How long an idle worker waits before looking again. A submit wakes it, so this only bounds
    // the case where every voice is already read ahead.
    private const int IdleMilliseconds = 5;

    private readonly Lock _gate = new();
    private readonly List<StreamedVoice> _voices = [];
    private readonly Dictionary<(int SampleRate, int Channels), Stack<StreamedVoice>> _pooled = [];
    private readonly AutoResetEvent _wake = new(false);
    private readonly Func<int, int, IPcmQueue> _queues;
    private readonly Thread _worker;

    private volatile bool _stopping;
    private bool _disposed;

    // queues opens a device queue per voice, by sample rate and channel count.
    internal AudioStreamer(Func<int, int, IPcmQueue> queues)
    {
        _queues = queues;
        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "Capsule audio streaming",
        };

        _worker.Start();
    }

    // Sounds a resident clip's samples as its own voice from startSeconds on, reusing a pooled voice
    // of the same format. The pooled voice's cursor is pointed at the samples, and a replay of a clip
    // already heard at this format allocates nothing. The `ended` callback is reported when the voice
    // is disposed, for a caller counting a resident clip's live voices.
    internal StreamedVoice Play(
        PcmAudio samples,
        in AudioClip clip,
        float gain,
        float pitch,
        float pan,
        bool loop,
        double startSeconds = 0.0,
        Action? ended = null)
    {
        StreamedVoice voice = Rent(samples.SampleRate, samples.Channels);
        voice.Start(samples, clip, gain, pitch, pan, loop, startSeconds, ended);

        return voice;
    }

    // Sounds source as its own voice from startSeconds on. A file handle is not poolable, so the
    // caller opens the source per play and the voice owns it from here on.
    internal StreamedVoice Play(
        IPcmSource source,
        in AudioClip clip,
        float gain,
        float pitch,
        float pan,
        bool loop,
        double startSeconds = 0.0,
        Action? ended = null)
    {
        StreamedVoice voice = Rent(source.SampleRate, source.Channels);
        voice.Start(source, clip, gain, pitch, pan, loop, startSeconds, ended);

        return voice;
    }

    internal void Add(StreamedVoice voice)
    {
        lock (_gate)
        {
            _voices.Add(voice);
        }

        Wake();
    }

    internal void Wake()
    {
        if (!_disposed)
        {
            _wake.Set();
        }
    }

    // Voices the worker has returned to the pool, waiting to be played again.
    internal int Pooled
    {
        get
        {
            lock (_gate)
            {
                int pooled = 0;
                foreach (Stack<StreamedVoice> voices in _pooled.Values)
                {
                    pooled += voices.Count;
                }

                return pooled;
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

        // The worker is joined, so this thread releases what it left.
        foreach (StreamedVoice voice in _voices)
        {
            voice.CloseSource();
            voice.CloseQueue();
        }

        foreach (Stack<StreamedVoice> pooled in _pooled.Values)
        {
            foreach (StreamedVoice voice in pooled)
            {
                voice.CloseQueue();
            }
        }

        _voices.Clear();
        _pooled.Clear();
        _wake.Dispose();
    }

    // A pooled voice of this format, else one built for it. A voice from the pool holds no source,
    // because the worker releases it before pooling it.
    private StreamedVoice Rent(int sampleRate, int channels)
    {
        lock (_gate)
        {
            if (_pooled.TryGetValue((sampleRate, channels), out Stack<StreamedVoice>? pooled) && pooled.Count > 0)
            {
                return pooled.Pop();
            }
        }

        return new StreamedVoice(this, _queues(sampleRate, channels), sampleRate, channels);
    }

    private void Work()
    {
        try
        {
            Decode();
        }
        catch (Exception failure)
        {
            // A decode fault silences its own voice inside Fill. Reaching here means the worker is
            // gone and every streamed voice from now on stays silent. This is not the frame's thread,
            // so it must not crash.
            Log.Warning($"audio: the streaming worker stopped and streamed sound falls silent. {failure.Message}");
        }
    }

    private void Decode()
    {
        List<StreamedVoice> pass = [];

        while (!_stopping)
        {
            pass.Clear();
            lock (_gate)
            {
                pass.AddRange(_voices);
            }

            bool worked = false;

            foreach (StreamedVoice voice in pass)
            {
                if (voice.Returning)
                {
                    // Released before it is pooled. The game thread takes from the pool, and a voice
                    // reaches it holding no source a decode could still read.
                    voice.CloseSource();

                    lock (_gate)
                    {
                        _voices.Remove(voice);
                        Return(voice);
                    }

                    worked = true;

                    continue;
                }

                worked |= voice.Fill();
            }

            if (!worked)
            {
                _wake.WaitOne(IdleMilliseconds);
            }
        }
    }

    // Under _gate.
    private void Return(StreamedVoice voice)
    {
        if (!_pooled.TryGetValue(voice.Format, out Stack<StreamedVoice>? pooled))
        {
            _pooled[voice.Format] = pooled = new Stack<StreamedVoice>();
        }

        pooled.Push(voice);
    }
}

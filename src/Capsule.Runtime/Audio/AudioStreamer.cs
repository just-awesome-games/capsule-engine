using Capsule.Audio;
using Capsule.Diagnostics;

namespace Capsule.Runtime.Audio;

// The one background thread every streamed voice decodes on, and the pool those voices come from.
// Decoding ahead off the game thread is the whole point of the thread: a frame never pays for a
// Vorbis packet, and a voice that falls behind starves its own device queue rather than the frame.
//
// A voice's source is released here rather than by whoever retired it, so nothing is ever freed under
// a decode in progress — and the release is what returns the voice to the pool, so a voice handed out
// again is one no decode still holds. The pool is keyed by format because a voice's queue and buffers
// are sized for one rate and channel count; it therefore holds no more voices than a run has sounded
// at once, and every replay of a format already heard allocates none of them.
internal sealed class AudioStreamer : IDisposable
{
    // How long an idle worker waits before looking again. A submit wakes it, so this only bounds
    // the case where every voice is already read ahead.
    private const int IdleMilliseconds = 5;

    private readonly Lock _gate = new();
    private readonly List<StreamedVoice> _voices = [];
    private readonly Dictionary<(int SampleRate, int Channels), Stack<StreamedVoice>> _idle = [];
    private readonly AutoResetEvent _wake = new(false);
    private readonly Func<int, int, IPcmQueue> _queues;
    private readonly Thread _worker;

    private volatile bool _stopping;
    private bool _disposed;

    // queues opens one device queue per voice, by sample rate and channel count.
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

    // Sounds a resident clip's samples as a voice of its own from startSeconds on, on a retired voice
    // of the same format where one is idle: that voice's own cursor is pointed at the samples, so a
    // replay of a clip already heard at this format allocates nothing at all. retired is reported
    // when the voice is disposed, for a caller counting a resident clip's live voices.
    internal StreamedVoice Play(
        PcmAudio samples,
        in AudioClip clip,
        float gain,
        float pitch,
        float pan,
        bool loop,
        double startSeconds = 0.0,
        Action? retired = null)
    {
        StreamedVoice voice = Take(samples.SampleRate, samples.Channels);
        voice.Start(samples, clip, gain, pitch, pan, loop, startSeconds, retired);

        return voice;
    }

    // Sounds source as a voice of its own from startSeconds on. The source is the caller's to open
    // per play — a file handle is not poolable — and the voice's from here on.
    internal StreamedVoice Play(
        IPcmSource source,
        in AudioClip clip,
        float gain,
        float pitch,
        float pan,
        bool loop,
        double startSeconds = 0.0,
        Action? retired = null)
    {
        StreamedVoice voice = Take(source.SampleRate, source.Channels);
        voice.Start(source, clip, gain, pitch, pan, loop, startSeconds, retired);

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

    // Voices retired, released by the worker, and waiting to be played again.
    internal int Idle
    {
        get
        {
            lock (_gate)
            {
                int idle = 0;
                foreach (Stack<StreamedVoice> voices in _idle.Values)
                {
                    idle += voices.Count;
                }

                return idle;
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

        // The worker is joined, so what it left is this thread's to release.
        foreach (StreamedVoice voice in _voices)
        {
            voice.ReleaseSource();
            voice.Release();
        }

        foreach (Stack<StreamedVoice> idle in _idle.Values)
        {
            foreach (StreamedVoice voice in idle)
            {
                voice.Release();
            }
        }

        _voices.Clear();
        _idle.Clear();
        _wake.Dispose();
    }

    // An idle voice of this format, else one built for it. A voice from the pool holds no source: the
    // worker releases it before pooling it.
    private StreamedVoice Take(int sampleRate, int channels)
    {
        lock (_gate)
        {
            if (_idle.TryGetValue((sampleRate, channels), out Stack<StreamedVoice>? idle) && idle.Count > 0)
            {
                return idle.Pop();
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
            // A decode fault silences its own voice inside Fill; reaching here means the worker
            // itself is gone, so every streamed voice from now on stays silent. Never a crash: this
            // is not the frame's thread.
            Log.Warning($"audio: the streaming worker stopped, so streamed sound falls silent — {failure.Message}");
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
                if (voice.Retiring)
                {
                    // Released before it is pooled, never after: the pool is what the game thread
                    // takes from, so a voice reaches it holding no source a decode could still read.
                    voice.ReleaseSource();

                    lock (_gate)
                    {
                        _voices.Remove(voice);
                        Pool(voice);
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
    private void Pool(StreamedVoice voice)
    {
        if (!_idle.TryGetValue(voice.Format, out Stack<StreamedVoice>? idle))
        {
            _idle[voice.Format] = idle = new Stack<StreamedVoice>();
        }

        idle.Push(voice);
    }
}

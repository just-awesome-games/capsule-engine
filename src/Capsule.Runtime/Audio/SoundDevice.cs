using Capsule.Diagnostics;

namespace Capsule.Runtime.Audio;

// A run's sound: the output the platform opened, the mixer and the thread that pushes it into the
// output, the worker that decodes streamed clips ahead, the resident clips and the game thread's
// voice table.
//
// An output that loses its device is closed and the platform's default opened again, on the game
// thread where the platform first opened it. A new mix thread runs the same mixer, and every voice
// plays on from where it was. Until an output opens, the mix runs into silence and keeps time.
//
// A mix thread that faults leaves the run silent for good. The game plays on, and nothing it asks for
// is posted to the mix again.
internal sealed class SoundDevice : IDisposable
{
    // How often the output is asked whether it still reaches a device, and a lost one reopened.
    internal const double WatchSeconds = 0.5;

    private readonly HostPlatform _platform;
    private readonly AudioStreamer _streamer = new();
    private readonly VoiceMixer _mixer;
    private AudioOutput _output;
    private MixThread _mix;

    // A fault taken from a mix thread the watch stopped, held for Update to log.
    private string? _fault;
    private bool _faulted;
    private double _sinceWatch;

    // Whether this streak of failed reopens has been warned of.
    private bool _reopenWarned;

    // Underruns of the mix threads already replaced, and whether the run's first has been warned of.
    private long _joinedUnderruns;
    private bool _underrunWarned;

    internal SoundStore Sounds { get; }

    internal AudioPlayer Player { get; }

    // Times this run's mix fell behind its output, which then played a gap.
    internal long Underruns => _joinedUnderruns + _mix.Underruns;

    // Blocks in which a streamed voice's decoding fell behind, which played that voice silent.
    internal long Starved => _mixer.Starved;

    private SoundDevice(HostPlatform platform, AudioOutput output)
    {
        _platform = platform;
        _output = output;

        AudioRing<MixCommand> commands = new(AudioPlayer.CommandCapacity);
        AudioRing<VoiceEnded> ended = new(AudioPlayer.EndedCapacity);
        Sounds = new SoundStore(clip => AudioFiles.ReadResident(platform, clip), clip => AudioFiles.OpenStream(platform, clip));
        Player = new AudioPlayer(Sounds, _streamer, commands, ended);
        _mixer = new VoiceMixer(commands, ended);
        _mix = new MixThread(_mixer, output);
    }

    // Opens the platform's output, or returns null for a silent run. A platform that could not open
    // its output has logged why. The game still runs, silently.
    internal static SoundDevice? TryOpen(HostPlatform platform)
    {
        if (platform.OpenAudioOutput() is not { } output)
        {
            Log.Debug("audio: the platform opened no output and the run is silent");

            return null;
        }

        return new SoundDevice(platform, output);
    }

    // Once a frame, on the game thread, with the wall seconds since the last. Reopens a lost output,
    // logs a fault on the mix thread or the decode worker, and retires what the mix thread reports
    // ended. A faulted mix silences the player once every ended voice it reported is retired.
    internal void Update(double elapsedSeconds)
    {
        if (!_faulted)
        {
            Watch(elapsedSeconds);
        }

        string? fault = _fault ?? _mix.TakeFault();
        _fault = null;

        _streamer.Update();
        Player.Update();

        if (!_underrunWarned && Underruns > 0)
        {
            Log.Warning("audio: the mix fell behind the device and a gap was heard. Later gaps are not logged");
            _underrunWarned = true;
        }

        if (fault is not null)
        {
            Log.Warning(fault);
            _faulted = true;
            Player.Silence();
        }
    }

    // The mix thread stops before the output closes under it, and the streams it read close last.
    public void Dispose()
    {
        _mix.Dispose();
        _output.Dispose();
        Sounds.Dispose();
        _streamer.Dispose();
    }

    private void Watch(double elapsedSeconds)
    {
        _sinceWatch += elapsedSeconds;
        if (_sinceWatch < WatchSeconds)
        {
            return;
        }

        _sinceWatch = 0.0;
        if (_output is SilentOutput)
        {
            if (Reopen() is { } reopened)
            {
                CloseOutput();
                MixInto(reopened);
            }
        }
        else if (!_output.Connected)
        {
            // The lost device closes before the platform opens the default again.
            CloseOutput();
            MixInto(Reopen() ?? new SilentOutput());
        }
    }

    // Joins the mix thread and closes the output it played into.
    private void CloseOutput()
    {
        _mix.Dispose();
        _output.Dispose();
    }

    // Starts a new mix thread over the same mixer into next, every voice where it was. A thread that
    // stopped on a fault is not replaced, and Update logs the fault and silences the run.
    private void MixInto(AudioOutput next)
    {
        _output = next;
        if (_mix.TakeFault() is { } fault)
        {
            _fault = fault;

            return;
        }

        _joinedUnderruns += _mix.Underruns;
        _mix = new MixThread(_mixer, next);
    }

    private AudioOutput? Reopen()
    {
        if (_platform.OpenAudioOutput() is { } output)
        {
            Log.Info("audio output reopened, and every voice plays on");
            _reopenWarned = false;

            return output;
        }

        if (!_reopenWarned)
        {
            Log.Warning("audio output lost and none could be reopened. The game runs silent and keeps retrying");
            _reopenWarned = true;
        }

        return null;
    }
}

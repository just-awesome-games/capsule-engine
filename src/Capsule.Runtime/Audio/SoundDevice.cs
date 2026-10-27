using Capsule.Diagnostics;

namespace Capsule.Runtime.Audio;

// A run's sound: the output the platform opened, the mixer and the thread that pushes it into the
// output, the worker that decodes streamed clips ahead, the resident clips and the game thread's
// voice table. The output follows the system's default device itself, and one output and one mix
// thread serve the whole run.
//
// A mix thread that faults leaves the run silent for good. The game plays on, and nothing it asks for
// is posted to the mix again.
internal sealed class SoundDevice : IDisposable
{
    private readonly AudioStreamer _streamer = new();
    private readonly VoiceMixer _mixer;
    private readonly AudioOutput _output;
    private readonly MixThread _mix;

    // Whether the run's first underrun has been warned of.
    private bool _underrunWarned;

    internal SoundStore Sounds { get; }

    internal AudioPlayer Player { get; }

    // Times this run's mix fell behind its output, which then played a gap.
    internal long Underruns => _mix.Underruns;

    // Blocks in which a streamed voice's decoding fell behind, which played that voice silent.
    internal long Starved => _mixer.Starved;

    private SoundDevice(HostPlatform platform, AudioOutput output, int leadFrames)
    {
        _output = output;

        AudioRing<MixCommand> commands = new(AudioPlayer.CommandCapacity);
        AudioRing<VoiceEnded> ended = new(AudioPlayer.EndedCapacity);
        Sounds = new SoundStore(clip => AudioFiles.ReadResident(platform, clip), clip => AudioFiles.OpenStream(platform, clip));
        Player = new AudioPlayer(Sounds, _streamer, commands, ended);
        _mixer = new VoiceMixer(commands, ended);

        // The game's lead or what the output's device needs, whichever is longer. A raised lead is logged.
        int lead = MixThread.LeadFor(leadFrames, output.DeviceFrames);
        if (lead > leadFrames)
        {
            Log.Info($"audio: the device takes {output.DeviceFrames} frames at a time, and the mix keeps {lead * 1000 / VoiceMixer.SampleRate} ms queued in place of the {leadFrames * 1000 / VoiceMixer.SampleRate} ms asked for");
        }

        _mix = new MixThread(_mixer, output, lead);
    }

    // Opens the platform's output, or returns null for a silent run. A platform that could not open
    // its output has logged why. The game still runs, silently.
    internal static SoundDevice? TryOpen(HostPlatform platform, int leadFrames)
    {
        if (platform.OpenAudioOutput(leadFrames) is not { } output)
        {
            Log.Debug("audio: the platform opened no output and the run is silent");

            return null;
        }

        return new SoundDevice(platform, output, leadFrames);
    }

    // Once a frame, on the game thread. Logs a fault on the mix thread or the decode worker, and retires
    // what the mix thread reports ended. A faulted mix silences the player once every ended voice it
    // reported is retired.
    internal void Update()
    {
        string? fault = _mix.TakeFault();

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
}

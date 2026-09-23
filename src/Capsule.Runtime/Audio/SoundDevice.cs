using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Runtime.Audio.Vorbis;
using Microsoft.Xna.Framework.Audio;

namespace Capsule.Runtime.Audio;

// The backend's sound device: clips decoded whole, clips decoded as they play, the worker that reads
// the latter ahead, and the watch that keeps the output on the system's default device.
internal sealed class SoundDevice : IAudioBackend
{
    private readonly AudioStreamer _streamer = new((rate, channels) => new DynamicPcmQueue(rate, channels));
    private readonly HostPlatform _platform;

    // Both null when the platform offers no way to follow the default output, which leaves sound
    // on the device the run opened.
    private AudioOutput? _output;
    private OutputFollower? _follower;

    // Whether the last reopen failed, which warns once over a streak of retries instead of every
    // frame.
    private bool _reopenFailed;

    private SoundDevice(HostPlatform platform) => _platform = platform;

    // Opens the device, or logs why and returns null. The game still runs, silently, on a machine with
    // no sound card, no output device or no audio runtime.
    internal static SoundDevice? TryOpen(HostPlatform platform)
    {
        try
        {
            SoundEffect.Initialize();
        }
        catch (NoAudioHardwareException failure)
        {
            return Silent(failure);
        }
        catch (DllNotFoundException failure)
        {
            return Silent(failure);
        }

        SoundDevice device = new(platform);
        device.FollowDefaultOutput();

        Log.Info(device._output is { } output ? $"audio device opened on '{output.Name}'" : "audio device opened");

        return device;
    }

    // Once a frame, on the game thread. Moves the output to the system's default device when the
    // default has changed or the device has been pulled.
    internal void Update(double elapsedSeconds) => _follower?.Update(elapsedSeconds);

    // MonoGame's master volume propagates to OpenAL gain for every resident and dynamic
    // SoundEffectInstance, so the host applies one gain across the output.
    internal void SetOutputGain(float gain) => SoundEffect.MasterVolume = gain;

    public MemoryStream Read(in AudioClip clip)
    {
        using Stream file = AudioFiles.Open(_platform, clip);
        MemoryStream bytes = file.CanSeek ? new((int)file.Length) : new();
        file.CopyTo(bytes);

        return bytes;
    }

    public IResidentSound Load(in AudioClip clip, MemoryStream file)
    {
        using (file)
        {
            file.Position = 0;

            return new ResidentSoundEffect(SoundEffect.FromStream(file), clip, _streamer, _platform);
        }
    }

    public IAudioVoice Stream(in AudioClip clip, float gain, float pitch, float pan, bool loop, double startSeconds)
    {
        VorbisReader reader = new(AudioFiles.Open(_platform, clip), closeOnDispose: true);

        // The device takes mono and stereo. Anything wider would need downmixing.
        if (reader.Channels is not (1 or 2))
        {
            int channels = reader.Channels;
            reader.Dispose();

            throw new NotSupportedException(
                $"Audio clip '{clip.Name}' is {channels}-channel. Capsule streams mono and stereo Ogg Vorbis.");
        }

        return _streamer.Play(new VorbisPcmSource(reader), clip, gain, pitch, pan, loop, startSeconds);
    }

    // The watch goes first, so no announcement lands while the voices it would move are torn down.
    public void Dispose()
    {
        _output?.Dispose();
        _output = null;
        _follower = null;
        _streamer.Dispose();
    }

    private static SoundDevice? Silent(Exception failure)
    {
        Log.Warning($"audio device unavailable and the game runs silent. {failure.Message}");

        return null;
    }

    private void FollowDefaultOutput()
    {
        OutputFollower follower = new(Reopen, () => _output is { Connected: true });
        _output = _platform.WatchDefaultAudioOutput(follower.DefaultChanged);

        if (_output is null)
        {
            Log.Debug("audio output cannot follow the default device, because the platform offers no watch over it");

            return;
        }

        _follower = follower;
    }

    private bool Reopen()
    {
        if (_output is not { } output)
        {
            return true;
        }

        if (output.TryReopen(out string? reason))
        {
            Log.Info($"audio output moved to '{output.Name}'");
            _reopenFailed = false;

            return true;
        }

        if (!_reopenFailed)
        {
            Log.Warning($"audio output could not move to the default device ({reason}), retrying");
            _reopenFailed = true;
        }

        return false;
    }
}

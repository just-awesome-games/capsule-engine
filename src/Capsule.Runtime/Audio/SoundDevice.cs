using Capsule.Audio;
using Capsule.Diagnostics;
using Microsoft.Xna.Framework.Audio;
using NVorbis;

namespace Capsule.Runtime.Audio;

// The OpenAL-backed sound device: clips decoded whole, clips decoded as they play, and the worker
// that reads the latter ahead.
internal sealed class SoundDevice : IAudioBackend
{
    private readonly AudioStreamer _streamer = new((rate, channels) => new DynamicPcmQueue(rate, channels));

    private SoundDevice()
    {
    }

    // Opens the device, or answers null having said why. A machine with no sound card, no output
    // device or no OpenAL runtime is a machine the game still runs on, silently.
    internal static SoundDevice? TryOpen()
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

        Log.Info("audio device opened");

        return new SoundDevice();
    }

    public IResidentSound Load(in AudioClip clip)
    {
        string path = AudioFiles.Locate(AppContext.BaseDirectory, clip);

        using FileStream file = File.OpenRead(path);

        return new ResidentSoundEffect(SoundEffect.FromStream(file), clip, _streamer);
    }

    public IAudioVoice Stream(in AudioClip clip, float gain, float pitch, float pan, bool loop, double startSeconds)
    {
        string path = AudioFiles.Locate(AppContext.BaseDirectory, clip);
        VorbisReader reader = new(File.OpenRead(path), closeOnDispose: true);

        // The device takes mono and stereo; anything wider would have to be downmixed, and no
        // shipped clip is.
        if (reader.Channels is not (1 or 2))
        {
            int channels = reader.Channels;
            reader.Dispose();

            throw new NotSupportedException(
                $"Audio clip '{clip.Name}' is {channels}-channel; Capsule streams mono and stereo Ogg Vorbis.");
        }

        return _streamer.Play(new VorbisPcmSource(reader), clip, gain, pitch, pan, loop, startSeconds);
    }

    public void Dispose() => _streamer.Dispose();

    private static SoundDevice? Silent(Exception failure)
    {
        Log.Warning($"audio device unavailable, so the game runs silent — {failure.Message}");

        return null;
    }
}

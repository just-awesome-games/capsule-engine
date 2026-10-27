using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Audio;

// The default platform's output: the host's mix played through an SDL3 audio stream on the system's
// default output. The host initialises SDL's video and gamepad subsystems and never audio, which this
// owns.
//
// The stream is bound to SDL's default playback device, which SDL moves to the new default when it
// changes or the old one is pulled. Pulled with no other device to move to, the stream plays into
// nothing on the clock until a device appears, and the output is never lost.
//
// The stream takes the host's own format, and SDL converts to whatever the hardware runs at. No
// callback is installed. The host puts frames on the stream, and SDL's device thread runs no managed
// code.
internal sealed class SdlAudioOutput : AudioOutput
{
    // The device's buffer, in frames, which SDL pulls from the stream at a time. SDL reads the hint as
    // it finds each device, and it is set before the subsystem starts. A driver may hold a period of its
    // own instead, as Windows' shared mode holds 10 ms.
    private const string DeviceFramesHint = "SDL_AUDIO_DEVICE_SAMPLE_FRAMES";
    private const int MaxDeviceFrames = 512;

    private readonly nint _stream;

    private SdlAudioOutput(nint stream) => _stream = stream;

    // A failed query reads -1 bytes, which truncates to no frames.
    protected internal override int QueuedFrames => Sdl3.SDL_GetAudioStreamQueued(_stream) / (sizeof(float) * Channels);

    // Opens the system's default output, or logs why it could not and returns null.
    internal static unsafe SdlAudioOutput? TryOpen(int leadFrames)
    {
        Sdl3.SDL_SetHint(DeviceFramesHint, BufferFramesFor(leadFrames).ToString(CultureInfo.InvariantCulture));

        if (!Sdl3.SDL_InitSubSystem(Sdl3.InitAudio))
        {
            Unavailable();

            return null;
        }

        Sdl3.AudioSpec spec = new()
        {
            Format = BitConverter.IsLittleEndian ? Sdl3.AudioFloat32Little : Sdl3.AudioFloat32Big,
            Channels = Channels,
            Frequency = SampleRate,
        };

        nint stream = Sdl3.SDL_OpenAudioDeviceStream(Sdl3.DefaultPlayback, in spec, nint.Zero, nint.Zero);
        if (stream == nint.Zero)
        {
            Unavailable();
            Sdl3.SDL_QuitSubSystem(Sdl3.InitAudio);

            return null;
        }

        Log.Info($"audio output opened on {DefaultName()} through SDL's '{Marshal.PtrToStringUTF8((nint)Sdl3.SDL_GetCurrentAudioDriver())}' driver");
        Sdl3.SDL_ResumeAudioStreamDevice(stream);

        return new SdlAudioOutput(stream);
    }

    // Destroying the stream closes the device it opened. Each output initialises the audio subsystem
    // once and quits it once, and SDL counts them.
    public override void Dispose()
    {
        Sdl3.SDL_DestroyAudioStream(_stream);
        Sdl3.SDL_QuitSubSystem(Sdl3.InitAudio);
    }

    // A failed query reads no frames, and the host keeps the lead it was given.
    protected internal override unsafe int DeviceFrames
    {
        get
        {
            Sdl3.AudioSpec spec;
            int frames;

            return Sdl3.SDL_GetAudioDeviceFormat(Sdl3.SDL_GetAudioStreamDevice(_stream), &spec, &frames) ? frames : 0;
        }
    }

    // The buffer asked for a lead: the largest power of two within half of it, at most 512 frames.
    internal static int BufferFramesFor(int leadFrames) => Math.Min(MaxDeviceFrames, 1 << BitOperations.Log2((uint)(leadFrames / 2)));

    // A failure leaves the thread at the host's own priority.
    protected internal override void RaiseMixThreadPriority() => Sdl3.SDL_SetCurrentThreadPriority(Sdl3.ThreadPriorityTimeCritical);

    protected internal override void Submit(ReadOnlySpan<float> frames) =>
        Sdl3.SDL_PutAudioStreamData(_stream, in MemoryMarshal.GetReference(frames), frames.Length * sizeof(float));

    // The default output's name in quotes.
    private static unsafe string DefaultName() =>
        Marshal.PtrToStringUTF8((nint)Sdl3.SDL_GetAudioDeviceName(Sdl3.DefaultPlayback)) is { Length: > 0 } name
            ? $"'{name}'"
            : "the system default";

    private static void Unavailable() =>
        Log.Warning($"audio output unavailable and the game runs silent. {Sdl3.Error()}");
}

using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Runtime.Audio;

namespace Capsule.Runtime.Desktop.Audio;

// The host's mix played through an SDL3 audio stream on the system's default output. The backend
// initialises SDL's video and input subsystems and never audio, which this owns.
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
    private const uint InitAudio = 0x00000010;

    // SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, the system default that SDL follows as it moves.
    private const uint DefaultPlayback = 0xFFFFFFFF;

    // SDL_AUDIO_F32LE and SDL_AUDIO_F32BE: 32-bit float samples in either byte order.
    private const uint Float32Little = 0x8120;
    private const uint Float32Big = 0x9120;

    // The device's buffer, in frames, which SDL pulls from the stream at a time. SDL reads the hint as
    // it finds each device, and it is set before the subsystem starts.
    private const string DeviceFramesHint = "SDL_AUDIO_DEVICE_SAMPLE_FRAMES";
    private const string DeviceFrames = "512";

    // SDL_THREAD_PRIORITY_TIME_CRITICAL. On Linux SDL asks RealtimeKit for a higher priority and keeps
    // the thread off real-time scheduling, whose CPU budget kills a process that overruns it.
    private const int TimeCritical = 3;

    private readonly nint _stream;

    private SdlAudioOutput(nint stream) => _stream = stream;

    // A failed query reads -1 bytes, which truncates to no frames.
    protected override int QueuedFrames => SDL_GetAudioStreamQueued(_stream) / (sizeof(float) * Channels);

    // Opens the system's default output, or logs why it could not and returns null.
    internal static SdlAudioOutput? TryOpen()
    {
        SdlPlatform.Load();
        SDL_SetHint(DeviceFramesHint, DeviceFrames);

        if (!SDL_InitSubSystem(InitAudio))
        {
            Unavailable();

            return null;
        }

        SdlAudioSpec spec = new()
        {
            Format = BitConverter.IsLittleEndian ? Float32Little : Float32Big,
            Channels = AudioOutput.Channels,
            Frequency = SampleRate,
        };

        nint stream = SDL_OpenAudioDeviceStream(DefaultPlayback, in spec, nint.Zero, nint.Zero);
        if (stream == nint.Zero)
        {
            Unavailable();
            SDL_QuitSubSystem(InitAudio);

            return null;
        }

        Log.Info($"audio output opened on {DefaultName()} through SDL's '{Marshal.PtrToStringUTF8(SDL_GetCurrentAudioDriver())}' driver");
        SDL_ResumeAudioStreamDevice(stream);

        return new SdlAudioOutput(stream);
    }

    // A failure leaves the thread at the host's own priority.
    protected override void RaiseMixThreadPriority() => SDL_SetCurrentThreadPriority(TimeCritical);

    protected override void Submit(ReadOnlySpan<float> frames) =>
        SDL_PutAudioStreamData(_stream, in MemoryMarshal.GetReference(frames), frames.Length * sizeof(float));

    // Destroying the stream closes the device it opened. Each output initialises the audio subsystem
    // once and quits it once, and SDL counts them.
    public override void Dispose()
    {
        SDL_DestroyAudioStream(_stream);
        SDL_QuitSubSystem(InitAudio);
    }

    // The default output's name in quotes. SDL owns the string.
    private static string DefaultName() =>
        Marshal.PtrToStringUTF8(SDL_GetAudioDeviceName(DefaultPlayback)) is { Length: > 0 } name
            ? $"'{name}'"
            : "the system default";

    private static void Unavailable() =>
        Log.Warning($"audio output unavailable and the game runs silent. {Error()}");

    private static string Error() => Marshal.PtrToStringUTF8(SDL_GetError()) ?? "SDL reported no error";

    // LibraryImport generates unsafe marshalling stubs, and these calls do not justify opening the
    // assembly to unsafe code. A stream is an opaque handle, the spec is blittable, the queued frames
    // pass as a pinned reference to their first sample, and SDL3's bool marshals as one byte.
#pragma warning disable SYSLIB1054
    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_SetHint")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_InitSubSystem")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_InitSubSystem(uint flags);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_QuitSubSystem")]
    private static extern void SDL_QuitSubSystem(uint flags);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_OpenAudioDeviceStream")]
    private static extern nint SDL_OpenAudioDeviceStream(uint device, in SdlAudioSpec spec, nint callback, nint userData);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_ResumeAudioStreamDevice")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_ResumeAudioStreamDevice(nint stream);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_PutAudioStreamData")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_PutAudioStreamData(nint stream, in float data, int length);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_GetAudioStreamQueued")]
    private static extern int SDL_GetAudioStreamQueued(nint stream);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_DestroyAudioStream")]
    private static extern void SDL_DestroyAudioStream(nint stream);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_GetAudioDeviceName")]
    private static extern nint SDL_GetAudioDeviceName(uint device);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_GetCurrentAudioDriver")]
    private static extern nint SDL_GetCurrentAudioDriver();

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_SetCurrentThreadPriority")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_SetCurrentThreadPriority(int priority);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_GetError")]
    private static extern nint SDL_GetError();
#pragma warning restore SYSLIB1054

    // SDL_AudioSpec: the sample format, the channels and the frame rate.
    [StructLayout(LayoutKind.Sequential)]
    private struct SdlAudioSpec
    {
        internal uint Format;
        internal int Channels;
        internal int Frequency;
    }
}

using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Runtime.Audio;

namespace Capsule.Runtime.Desktop.Audio;

// The host's mix played through SDL's audio output, on the SDL library the backend already ships. The
// backend initialises SDL's video and input subsystems and never audio, which this owns.
//
// The device is opened by no name, which SDL takes as the system default and moves to the new default
// when it changes or the old one is pulled. Pulled with no other device to move to, it stops for good,
// and the host reopens the default once Connected reads false.
//
// It is opened at the host's own format with no changes allowed, and SDL converts to whatever the
// hardware runs at. No callback is installed. The host queues frames, and SDL's device thread runs no
// managed code.
internal sealed class SdlAudioOutput : AudioOutput
{
    private const uint InitAudio = 0x00000010;

    // AUDIO_F32LSB and AUDIO_F32MSB: 32-bit float samples in either byte order.
    private const ushort Float32Little = 0x8120;
    private const ushort Float32Big = 0x9120;

    // The device's buffer, in frames. SDL pulls from the queue this many at a time.
    private const ushort DeviceFrames = 512;

    // SDL_AUDIO_STOPPED, the status of a device SDL has given up on.
    private const int Stopped = 0;

    // Whether the last open failed. The host retries a lost output on an interval, and a streak of
    // failures warns once. Read and written on the game thread alone.
    private static bool LastOpenFailed;

    private readonly uint _device;

    private SdlAudioOutput(uint device) => _device = device;

    protected override int QueuedFrames => (int)(SDL_GetQueuedAudioSize(_device) / (sizeof(float) * Channels));

    // A device SDL has stopped stays stopped.
    protected override bool Connected => SDL_GetAudioDeviceStatus(_device) != Stopped;

    // Opens the system's default output, or logs why it could not and returns null.
    internal static SdlAudioOutput? TryOpen()
    {
        SdlPlatform.UseResolver();

        if (SDL_InitSubSystem(InitAudio) != 0)
        {
            Unavailable();

            return null;
        }

        SdlAudioSpec wanted = new()
        {
            Frequency = SampleRate,
            Format = BitConverter.IsLittleEndian ? Float32Little : Float32Big,
            Channels = (byte)AudioOutput.Channels,
            Samples = DeviceFrames,
        };

        uint device = SDL_OpenAudioDevice(nint.Zero, 0, ref wanted, out _, 0);
        if (device == 0)
        {
            Unavailable();
            SDL_QuitSubSystem(InitAudio);

            return null;
        }

        Log.Info($"audio output opened on {DefaultName()} through SDL's '{Marshal.PtrToStringUTF8(SDL_GetCurrentAudioDriver())}' driver");
        SDL_PauseAudioDevice(device, 0);
        LastOpenFailed = false;

        return new SdlAudioOutput(device);
    }

    protected override void Submit(ReadOnlySpan<float> frames) =>
        SDL_QueueAudio(_device, in MemoryMarshal.GetReference(frames), (uint)(frames.Length * sizeof(float)));

    // Each output initialises the audio subsystem once and quits it once. SDL counts them, and a
    // reopen that closes the lost output first quits and initialises it again.
    public override void Dispose()
    {
        SDL_CloseAudioDevice(_device);
        SDL_QuitSubSystem(InitAudio);
    }

    // The default output's name in quotes, from SDL 2.24 on, or a plain description before it.
    private static string DefaultName()
    {
        try
        {
            if (SDL_GetDefaultAudioInfo(out nint name, out _, 0) == 0 && name != nint.Zero)
            {
                string named = Marshal.PtrToStringUTF8(name) ?? "";
                SDL_free(name);

                return $"'{named}'";
            }
        }
        catch (EntryPointNotFoundException)
        {
        }

        return "the system default";
    }

    private static void Unavailable()
    {
        string message = $"audio output unavailable and the game runs silent. {Error()}";
        if (LastOpenFailed)
        {
            Log.Debug(message);
        }
        else
        {
            Log.Warning(message);
        }

        LastOpenFailed = true;
    }

    private static string Error() => Marshal.PtrToStringUTF8(SDL_GetError()) ?? "SDL reported no error";

    // LibraryImport generates unsafe marshalling stubs, and these calls do not justify opening the
    // assembly to unsafe code. A device is an id, the spec is blittable, and the queued frames pass as
    // a pinned reference to their first sample.
#pragma warning disable SYSLIB1054
    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_InitSubSystem")]
    private static extern int SDL_InitSubSystem(uint flags);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_QuitSubSystem")]
    private static extern void SDL_QuitSubSystem(uint flags);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_OpenAudioDevice")]
    private static extern uint SDL_OpenAudioDevice(nint device, int isCapture, ref SdlAudioSpec desired, out SdlAudioSpec obtained, int allowedChanges);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_PauseAudioDevice")]
    private static extern void SDL_PauseAudioDevice(uint device, int pauseOn);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_QueueAudio")]
    private static extern int SDL_QueueAudio(uint device, in float data, uint length);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_GetQueuedAudioSize")]
    private static extern uint SDL_GetQueuedAudioSize(uint device);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_GetAudioDeviceStatus")]
    private static extern int SDL_GetAudioDeviceStatus(uint device);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_CloseAudioDevice")]
    private static extern void SDL_CloseAudioDevice(uint device);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_GetDefaultAudioInfo")]
    private static extern int SDL_GetDefaultAudioInfo(out nint name, out SdlAudioSpec spec, int isCapture);

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_GetCurrentAudioDriver")]
    private static extern nint SDL_GetCurrentAudioDriver();

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_GetError")]
    private static extern nint SDL_GetError();

    [DllImport(SdlPlatform.LibraryName, EntryPoint = "SDL_free")]
    private static extern void SDL_free(nint memory);
#pragma warning restore SYSLIB1054

    // SDL_AudioSpec, fixed by SDL2's ABI. Silence and size are SDL's to fill, and no callback is set.
    [StructLayout(LayoutKind.Sequential)]
    private struct SdlAudioSpec
    {
        internal int Frequency;
        internal ushort Format;
        internal byte Channels;
        internal byte Silence;
        internal ushort Samples;
        internal ushort Padding;
        internal uint Size;
        internal nint Callback;
        internal nint UserData;
    }
}

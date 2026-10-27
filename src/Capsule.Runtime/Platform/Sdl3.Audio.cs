using System.Runtime.InteropServices;

namespace Capsule.Runtime.Platform;

// The audio stream the default output plays the host's mix through, and its thread's priority.
internal static unsafe partial class Sdl3
{
    internal const uint InitAudio = 0x00000010;

    // SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, the system default that SDL follows as it moves.
    internal const uint DefaultPlayback = 0xFFFFFFFF;

    // SDL_AUDIO_F32LE and SDL_AUDIO_F32BE: 32-bit float samples in either byte order.
    internal const uint AudioFloat32Little = 0x8120;
    internal const uint AudioFloat32Big = 0x9120;

    // SDL_THREAD_PRIORITY_TIME_CRITICAL. On Linux SDL asks RealtimeKit for a higher priority and keeps
    // the thread off real-time scheduling, whose CPU budget kills a process that overruns it.
    internal const int ThreadPriorityTimeCritical = 3;

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_OpenAudioDeviceStream(uint device, in AudioSpec spec, nint callback, nint userData);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_ResumeAudioStreamDevice(nint stream);

    // The queued frames pass as a pinned reference to their first sample.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_PutAudioStreamData(nint stream, in float data, int length);

    // The bytes queued, or -1 on failure.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern int SDL_GetAudioStreamQueued(nint stream);

    // The device a stream plays on, or 0 when it is bound to none.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint SDL_GetAudioStreamDevice(nint stream);

    // The device's format and the frames it takes from its streams at a time.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetAudioDeviceFormat(uint device, AudioSpec* spec, int* sampleFrames);

    // Also closes the device the stream opened.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_DestroyAudioStream(nint stream);

    // SDL owns the string.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetAudioDeviceName(uint device);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetCurrentAudioDriver();

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetCurrentThreadPriority(int priority);

    // SDL_AudioSpec: the sample format, the channels and the frame rate.
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioSpec
    {
        public uint Format;
        public int Channels;
        public int Frequency;
    }
}

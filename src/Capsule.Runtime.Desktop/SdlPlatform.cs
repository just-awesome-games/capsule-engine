using System.Reflection;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;

namespace Capsule.Runtime.Desktop;

// The SDL calls the host makes for itself. The graphics backend keeps its own binding internal and
// initialises a fixed set of subsystems, so these calls are made here against the library the backend
// already ships beside the executable.
internal static class SdlPlatform
{
    private const string LibraryName = "SDL2";

    private const uint InputFocusFlag = 0x00000200;
    private const uint WindowEventType = 0x200;
    private const byte WindowExposed = 3;
    private const byte WindowSizeChanged = 6;

    // Byte offsets into SDL_WindowEvent, fixed by SDL2's ABI: the union's type, then the per-window
    // event id after the timestamp and window id.
    private const int TypeOffset = 0;
    private const int WindowEventIdOffset = 12;

    static SdlPlatform() => NativeLibrary.SetDllImportResolver(typeof(SdlPlatform).Assembly, Resolve);

    private delegate int SdlEventFilter(nint userData, nint sdlEvent);

    // Brings the window to the front and asks for keyboard focus. Windows grants foreground
    // activation only to a process that already holds it, and a launch from a busy terminal can still
    // leave the window behind that terminal.
    internal static void RaiseWindow(nint window) => SDL_RaiseWindow(window);

    // SDL's own keyboard-focus flag, false from creation until the OS grants focus. The backend's
    // IsActive reports true from construction until the first focus event, and a window that never
    // gained focus would claim the global mouse and play at full volume.
    internal static bool HasInputFocus(nint window) => (SDL_GetWindowFlags(window) & InputFocusFlag) != 0;

    // The operating system's own handle for the window, an HWND on Windows, or zero when SDL will
    // not report one. A backend window handle is SDL's own opaque pointer, not this.
    internal static nint NativeWindowHandle(nint window)
    {
        SdlWindowInfo info = default;
        SDL_GetVersion(out info.Version);

        return SDL_GetWindowWMInfo(window, ref info) == 1 ? info.NativeWindow : nint.Zero;
    }

    // The window's current extent in screen coordinates, which SDL updates as the OS reports it,
    // during a modal resize and ahead of the event announcing the new size.
    internal static void WindowSize(nint window, out int width, out int height) =>
        SDL_GetWindowSize(window, out width, out height);

    // The default probe derives no candidate matching the versioned sonames the backend ships, so the
    // file is named per platform, as the backend's own loader names it.
    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return nint.Zero;
        }

        string[] candidates =
            OperatingSystem.IsWindows() ? ["SDL2.dll"]
            : OperatingSystem.IsMacOS() ? ["libSDL2-2.0.0.dylib", "libSDL2.dylib"]
            : ["libSDL2-2.0.so.0", "libSDL2.so"];

        foreach (string candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out nint handle))
            {
                return handle;
            }
        }

        return nint.Zero;
    }

    // An installed SDL event watch, which calls back whenever its window is resized or exposed.
    // Windows runs a window drag in an OS modal loop that blocks the game loop for its duration, and a
    // watch is the only callback SDL still delivers there. SDL delivers it synchronously on whichever
    // thread pushed the event, and a call from a thread other than the installing one, which owns the
    // window and the graphics device, is dropped.
    internal sealed class RedrawWatch : IDisposable
    {
        // Rooted for the watch's lifetime. SDL holds the thunk this marshals to, which the
        // collector cannot see, and removing the watch needs the delegate the install used.
        private readonly SdlEventFilter _filter;
        private readonly Action _redraw;
        private readonly int _thread;
        private bool _disposed;

        internal RedrawWatch(Action redraw)
        {
            _redraw = redraw;
            _thread = Environment.CurrentManagedThreadId;
            _filter = OnSdlEvent;
            SDL_AddEventWatch(_filter, nint.Zero);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SDL_DelEventWatch(_filter, nint.Zero);
        }

        // The return value is ignored for a watch. SDL consults it only for the event filter proper.
        private int OnSdlEvent(nint userData, nint sdlEvent)
        {
            if (_disposed || sdlEvent == nint.Zero || Environment.CurrentManagedThreadId != _thread)
            {
                return 0;
            }

            try
            {
                if ((uint)Marshal.ReadInt32(sdlEvent, TypeOffset) != WindowEventType)
                {
                    return 0;
                }

                if (Marshal.ReadByte(sdlEvent, WindowEventIdOffset) is WindowSizeChanged or WindowExposed)
                {
                    _redraw();
                }
            }
            catch (Exception error)
            {
                // An exception unwinding into SDL's own stack terminates the process. A failed redraw
                // costs only the frame it was drawing.
                Log.Warning($"Redrawing during a window resize failed: {error.Message}");
            }

            return 0;
        }
    }

    // LibraryImport generates unsafe marshalling stubs, and these calls do not justify opening the
    // assembly to unsafe code. A window and an event are opaque handles, already pointers, and the
    // filter is a non-generic delegate of blittable arguments, which marshals ahead of time.
#pragma warning disable SYSLIB1054
    [DllImport(LibraryName, EntryPoint = "SDL_RaiseWindow")]
    private static extern void SDL_RaiseWindow(nint window);

    [DllImport(LibraryName, EntryPoint = "SDL_GetWindowFlags")]
    private static extern uint SDL_GetWindowFlags(nint window);

    [DllImport(LibraryName, EntryPoint = "SDL_GetWindowSize")]
    private static extern void SDL_GetWindowSize(nint window, out int width, out int height);

    [DllImport(LibraryName, EntryPoint = "SDL_AddEventWatch")]
    private static extern void SDL_AddEventWatch(SdlEventFilter filter, nint userData);

    [DllImport(LibraryName, EntryPoint = "SDL_DelEventWatch")]
    private static extern void SDL_DelEventWatch(SdlEventFilter filter, nint userData);

    [DllImport(LibraryName, EntryPoint = "SDL_GetVersion")]
    private static extern void SDL_GetVersion(out SdlVersion version);

    // SDL refuses the call unless the version field names a release it can answer for, so the struct
    // is stamped from SDL_GetVersion before every call.
    [DllImport(LibraryName, EntryPoint = "SDL_GetWindowWMInfo")]
    private static extern int SDL_GetWindowWMInfo(nint window, ref SdlWindowInfo info);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlVersion
    {
        internal byte Major;
        internal byte Minor;
        internal byte Patch;
    }

    // SDL_SysWMinfo. The first member of its per-platform union is the native window on every platform
    // that has one. The declared size covers the union, which SDL writes through whichever driver
    // answered.
    [StructLayout(LayoutKind.Sequential, Size = 128)]
    private struct SdlWindowInfo
    {
        internal SdlVersion Version;
        internal int Subsystem;
        internal nint NativeWindow;
    }
}

using System.Reflection;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;

namespace Capsule.Runtime;

// The SDL calls the host makes for itself. The graphics backend keeps its own binding internal and
// initialises a fixed set of subsystems, so both of these have to be made here; the library is the
// backend's own, already resident beside the executable.
internal static class SdlPlatform
{
    private const string LibraryName = "SDL2";

    private const uint WindowEventType = 0x200;
    private const byte WindowExposed = 3;
    private const byte WindowSizeChanged = 6;

    // Byte offsets into SDL_WindowEvent, which SDL2's ABI fixes: the union's type, then the
    // per-window event id after the timestamp and window id.
    private const int TypeOffset = 0;
    private const int WindowEventIdOffset = 12;

    // Rooted for the watch's lifetime. SDL holds the thunk this marshals to, which the collector
    // cannot see, and removing the watch needs the same delegate the install used.
    private static SdlEventFilter? Watch;
    private static Action? Redraw;
    private static int WatchThread;

    static SdlPlatform() => NativeLibrary.SetDllImportResolver(typeof(SdlPlatform).Assembly, Resolve);

    private delegate int SdlEventFilter(nint userData, nint sdlEvent);

    // Brings the window to the front and asks for keyboard focus. Windows grants foreground
    // activation only to a process that already holds it, so a launch from a busy terminal can
    // still leave the window behind that terminal.
    internal static void RaiseWindow(nint window) => SDL_RaiseWindow(window);

    // The window's current extent in screen coordinates, which SDL updates as the OS reports it —
    // during a modal resize, ahead of the event announcing the new size.
    internal static void WindowSize(nint window, out int width, out int height) =>
        SDL_GetWindowSize(window, out width, out height);

    // Calls redraw whenever the window is resized or exposed, from inside SDL's own event handling
    // rather than from the next pumped frame. Windows runs a window drag in an OS modal loop that
    // blocks the game loop for its whole duration, and a watch is the one callback SDL still
    // delivers there — synchronously, on the thread that installed it, which is why a redraw from
    // here reaches the same device the frame loop owns. The callback is told nothing about the
    // event: it reads the window's extent itself, so a redraw already in flight may drop the event
    // that arrives during it without the view going stale. Called once, from the thread that owns
    // the window.
    internal static void WatchWindowRedraw(Action redraw)
    {
        if (Watch is not null)
        {
            return;
        }

        Redraw = redraw;
        WatchThread = Environment.CurrentManagedThreadId;
        Watch = OnSdlEvent;
        SDL_AddEventWatch(Watch, nint.Zero);
    }

    // Releases the watch. Idempotent, and safe to call when none was installed.
    internal static void StopWatchingWindowRedraw()
    {
        if (Watch is not { } watch)
        {
            return;
        }

        SDL_DelEventWatch(watch, nint.Zero);
        Watch = null;
        Redraw = null;
    }

    // The return value is ignored for a watch; SDL only consults it for the event filter proper.
    private static int OnSdlEvent(nint userData, nint sdlEvent)
    {
        // SDL delivers a watch on whichever thread pushed the event, and only the installing thread
        // owns the window and the graphics device.
        if (Redraw is not { } redraw || sdlEvent == nint.Zero || Environment.CurrentManagedThreadId != WatchThread)
        {
            return 0;
        }

        try
        {
            if ((uint)Marshal.ReadInt32(sdlEvent, TypeOffset) != WindowEventType)
            {
                return 0;
            }

            byte id = Marshal.ReadByte(sdlEvent, WindowEventIdOffset);
            if (id is not (WindowSizeChanged or WindowExposed))
            {
                return 0;
            }

            redraw();
        }
        catch (Exception error)
        {
            // An exception unwinding into SDL's own stack terminates the process, so a redraw that
            // fails costs the frame it was drawing and nothing more.
            Log.Warning($"Redrawing during a window resize failed: {error.Message}");
        }

        return 0;
    }

    // The default probe derives no candidate that matches the versioned sonames the backend ships,
    // so the file is named outright, per platform, as the backend's own loader names it.
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

    // LibraryImport generates unsafe marshalling stubs, and these do not justify opening the whole
    // assembly to unsafe code: a window and an event are opaque handles, pointers already, and the
    // filter is a non-generic delegate of blittable arguments, which marshals ahead of time.
#pragma warning disable SYSLIB1054
    [DllImport(LibraryName, EntryPoint = "SDL_RaiseWindow")]
    private static extern void SDL_RaiseWindow(nint window);

    [DllImport(LibraryName, EntryPoint = "SDL_GetWindowSize")]
    private static extern void SDL_GetWindowSize(nint window, out int width, out int height);

    [DllImport(LibraryName, EntryPoint = "SDL_AddEventWatch")]
    private static extern void SDL_AddEventWatch(SdlEventFilter filter, nint userData);

    [DllImport(LibraryName, EntryPoint = "SDL_DelEventWatch")]
    private static extern void SDL_DelEventWatch(SdlEventFilter filter, nint userData);
#pragma warning restore SYSLIB1054
}

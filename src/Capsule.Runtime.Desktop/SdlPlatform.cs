using System.Reflection;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;

namespace Capsule.Runtime.Desktop;

// The SDL3 calls the host makes for itself. The graphics backend keeps its own SDL2 binding, which
// sdl2-compat forwards to this same SDL3, and initialises a fixed set of subsystems. The bridge hands
// out SDL3's own windows, and a backend window handle is an SDL3 window. These calls read state and
// watch events. The backend pumps them.
internal static class SdlPlatform
{
    // Every SDL binding in this assembly names this library, which the resolver below maps to the
    // SDL3 the static constructor loads.
    internal const string LibraryName = "SDL3";

    // The bridge loads SDL3 by this file name alone. Windows and Linux look for it beside the
    // executable and in system folders, never beside the bridge, and a development build keeps both
    // libraries under runtimes/. Loading the game's own SDL3 first makes that name find it.
    private static readonly string Sdl3File =
        OperatingSystem.IsWindows() ? "SDL3.dll"
        : OperatingSystem.IsMacOS() ? "libSDL3.dylib"
        : "libSDL3.so.0";

    // The game's own SDL3, or zero when the load failed and a binding's first call reports it.
    internal static readonly nint Library;

    private const ulong MinimizedFlag = 0x40;
    private const ulong InputFocusFlag = 0x200;

    // The bridge's own event filter consumes SDL3's window events and posts each again as SDL2's
    // window event, its SDL2 fields copied after SDL3's 16-byte header. A watch sees only the repost:
    // this type at the event's start, and the SDL2 window event's id after its window id.
    private const uint BridgeWindowEvent = 0x200;
    private const int BridgeWindowEventIdOffset = 20;
    private const byte BridgeExposed = 3;
    private const byte BridgeSizeChanged = 6;

    private const string Win32WindowProperty = "SDL.window.win32.hwnd";

    // Byte offsets into SDL_DisplayMode: the extent follows the display's id and its pixel format.
    private const int ModeWidthOffset = 8;
    private const int ModeHeightOffset = 12;

    static SdlPlatform()
    {
        NativeLibrary.TryLoad(Sdl3File, typeof(SdlPlatform).Assembly, null, out Library);
        NativeLibrary.SetDllImportResolver(typeof(SdlPlatform).Assembly, Resolve);
    }

    // SDL3's bool is one byte.
    private delegate byte SdlEventFilter(nint userData, nint sdlEvent);

    // Loads SDL3 and registers the resolver for the whole assembly, once, in this class's static
    // constructor. The backend loads the bridge before the platform's first call, and a binding
    // outside this class needs the resolver before its own first call.
    internal static void Load()
    {
    }

    // Brings the window to the front and asks for keyboard focus. Windows may refuse it.
    internal static void RaiseWindow(nint window) => SDL_RaiseWindow(window);

    // SDL's own keyboard-focus flag, false from creation until the OS grants focus. The backend's
    // IsActive reports true from construction until the first focus event, and a window that never
    // gained focus would claim the global mouse and play at full volume.
    internal static bool HasInputFocus(nint window) => (SDL_GetWindowFlags(window) & InputFocusFlag) != 0;

    internal static bool IsMinimized(nint window) => (SDL_GetWindowFlags(window) & MinimizedFlag) != 0;

    // SDL releases the grab while the window is unfocused and takes it again on focus return.
    internal static void ConfineCursor(nint window, bool confined) => SDL_SetWindowMouseGrab(window, confined);

    // The window's HWND on Windows, or zero elsewhere or when SDL will not report one. A backend
    // window handle is SDL's own opaque pointer, not this.
    internal static nint NativeWindowHandle(nint window) =>
        OperatingSystem.IsWindows()
            ? SDL_GetPointerProperty(SDL_GetWindowProperties(window), Win32WindowProperty, nint.Zero)
            : nint.Zero;

    // The window's current extent in screen coordinates, which SDL updates as the OS reports it,
    // during a modal resize and ahead of the event announcing the new size.
    internal static void WindowSize(nint window, out int width, out int height) =>
        SDL_GetWindowSize(window, out width, out height);

    // The desktop extent of the display the window is on, or false when SDL cannot say. The mode is
    // SDL's own and is not freed.
    internal static bool DisplayExtent(nint window, out int width, out int height)
    {
        uint display = SDL_GetDisplayForWindow(window);
        nint mode = display == 0 ? nint.Zero : SDL_GetDesktopDisplayMode(display);
        if (mode == nint.Zero)
        {
            (width, height) = (0, 0);
            return false;
        }

        (width, height) = (Marshal.ReadInt32(mode, ModeWidthOffset), Marshal.ReadInt32(mode, ModeHeightOffset));
        return true;
    }

    // The operating system's preferred languages as BCP 47 tags, most preferred first. SDL answers
    // before it is initialised, and a failed call yields none.
    internal static IReadOnlyList<string> PreferredLocales()
    {
        nint locales = SDL_GetPreferredLocales(out int count);
        if (locales == nint.Zero)
        {
            return [];
        }

        // One allocation holds the array of SDL_Locale pointers and the locales they point to. An
        // SDL_Locale is two string pointers, the country null when the locale names none.
        try
        {
            string[] tags = new string[count];
            for (int index = 0; index < count; index++)
            {
                nint locale = Marshal.ReadIntPtr(locales, index * nint.Size);
                tags[index] = LocaleTag(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(locale))!, Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(locale, nint.Size)));
            }

            return tags;
        }
        finally
        {
            SDL_free(locales);
        }
    }

    // One SDL_Locale as a tag: the language alone, or the language and its country.
    internal static string LocaleTag(string language, string? country) =>
        string.IsNullOrEmpty(country) ? language : $"{language}-{country}";

    // The default probe derives no candidate matching SDL3's versioned soname.
    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        libraryName == LibraryName ? Library : nint.Zero;

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
            SDL_RemoveEventWatch(_filter, nint.Zero);
        }

        // The return value is ignored for a watch. SDL consults it only for the event filter proper.
        private byte OnSdlEvent(nint userData, nint sdlEvent)
        {
            if (_disposed || sdlEvent == nint.Zero || Environment.CurrentManagedThreadId != _thread)
            {
                return 0;
            }

            try
            {
                if ((uint)Marshal.ReadInt32(sdlEvent) == BridgeWindowEvent
                    && Marshal.ReadByte(sdlEvent, BridgeWindowEventIdOffset) is BridgeSizeChanged or BridgeExposed)
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
    // assembly to unsafe code. A window and an event are opaque handles, already pointers, the filter
    // is a non-generic delegate of blittable arguments, which marshals ahead of time, and SDL3's bool
    // marshals as one byte.
#pragma warning disable SYSLIB1054
    [DllImport(LibraryName, EntryPoint = "SDL_RaiseWindow")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_RaiseWindow(nint window);

    [DllImport(LibraryName, EntryPoint = "SDL_GetWindowFlags")]
    private static extern ulong SDL_GetWindowFlags(nint window);

    [DllImport(LibraryName, EntryPoint = "SDL_SetWindowMouseGrab")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_SetWindowMouseGrab(nint window, [MarshalAs(UnmanagedType.U1)] bool grabbed);

    [DllImport(LibraryName, EntryPoint = "SDL_GetWindowSize")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_GetWindowSize(nint window, out int width, out int height);

    [DllImport(LibraryName, EntryPoint = "SDL_GetWindowProperties")]
    private static extern uint SDL_GetWindowProperties(nint window);

    [DllImport(LibraryName, EntryPoint = "SDL_GetPointerProperty")]
    private static extern nint SDL_GetPointerProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint defaultValue);

    [DllImport(LibraryName, EntryPoint = "SDL_GetDisplayForWindow")]
    private static extern uint SDL_GetDisplayForWindow(nint window);

    [DllImport(LibraryName, EntryPoint = "SDL_GetDesktopDisplayMode")]
    private static extern nint SDL_GetDesktopDisplayMode(uint display);

    [DllImport(LibraryName, EntryPoint = "SDL_AddEventWatch")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_AddEventWatch(SdlEventFilter filter, nint userData);

    [DllImport(LibraryName, EntryPoint = "SDL_RemoveEventWatch")]
    private static extern void SDL_RemoveEventWatch(SdlEventFilter filter, nint userData);

    [DllImport(LibraryName, EntryPoint = "SDL_GetPreferredLocales")]
    private static extern nint SDL_GetPreferredLocales(out int count);

    [DllImport(LibraryName, EntryPoint = "SDL_free")]
    private static extern void SDL_free(nint memory);
#pragma warning restore SYSLIB1054
}

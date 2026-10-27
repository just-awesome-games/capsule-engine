using System.Runtime.InteropServices;

namespace Capsule.Runtime.Platform;

// The window, its display, the main callbacks and the frame's sleep.
internal static unsafe partial class Sdl3
{
    internal const uint InitVideo = 0x00000020;

    internal const uint EventQuit = 0x100;
    internal const uint EventDisplayDesktopModeChanged = 0x155;
    internal const uint EventWindowRestored = 0x20B;
    internal const uint EventWindowDisplayChanged = 0x213;
    internal const uint EventWindowDisplayScaleChanged = 0x214;
    internal const uint EventWindowLeaveFullscreen = 0x218;

    // SDL_WindowFlags.
    internal const ulong WindowFullscreen = 0x1;
    internal const ulong WindowHidden = 0x8;
    internal const ulong WindowResizable = 0x20;
    internal const ulong WindowMinimized = 0x40;
    internal const ulong WindowMaximized = 0x80;
    internal const ulong WindowInputFocus = 0x200;
    internal const ulong WindowHighPixelDensity = 0x2000;

    // SDL_MESSAGEBOX_ERROR.
    internal const uint MessageBoxError = 0x10;

    // SDL_AppResult.
    internal enum AppResult
    {
        Continue,
        Success,
        Failure,
    }

    // Runs SDL's main loop on this thread until a callback returns other than Continue, then quits SDL.
    // Each iteration pumps the events, hands each to the event callback, then calls the iterate
    // callback. A Windows move or resize calls the iterate callback alone, from inside the operating
    // system's modal loop.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern int SDL_EnterAppMainCallbacks(
        int argc,
        byte** argv,
        delegate* unmanaged<nint*, int, byte**, AppResult> init,
        delegate* unmanaged<nint, AppResult> iterate,
        delegate* unmanaged<nint, nint, AppResult> sdlEvent,
        delegate* unmanaged<nint, AppResult, void> quit);

    // Sleeps on the operating system's high-resolution timer where it has one, and never spins.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_DelayNS(ulong nanoseconds);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateWindow(byte* title, int width, int height, ulong flags);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_DestroyWindow(nint window);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_ShowWindow(nint window);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_RaiseWindow(nint window);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern ulong SDL_GetWindowFlags(nint window);

    // SDL copies the surface, which the caller still owns.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowIcon(nint window, nint surface);

    // Borderless at the display's desktop mode, since the window has no exclusive mode set.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowFullscreen(nint window, [MarshalAs(UnmanagedType.U1)] bool fullscreen);

    // In window coordinates. Requested while fullscreen, it is the size the window leaves fullscreen at.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowSize(nint window, int width, int height);

    // The window's drawable extent in pixels, which the swapchain takes on its next acquire.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetWindowSizeInPixels(nint window, int* width, int* height);

    // Pixels per window coordinate: 1 where the window is measured in pixels, 2 on a Retina display.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern float SDL_GetWindowPixelDensity(nint window);

    // The pixel density times the display's content scale, 1 at 100%.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern float SDL_GetWindowDisplayScale(nint window);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint SDL_GetDisplayForWindow(nint window);

    // SDL's own mode, which the caller never frees. Null when SDL cannot say.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern DisplayMode* SDL_GetDesktopDisplayMode(uint display);

    // SDL releases the grab while the window is unfocused and takes it again on focus return.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetWindowMouseGrab(nint window, [MarshalAs(UnmanagedType.U1)] bool grabbed);

    // A modal box with one button, which SDL can show before it initializes and after it quits.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_ShowSimpleMessageBox(
        uint flags,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message,
        nint window);

    // The extent is in window coordinates, and PixelDensity converts it to pixels.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayMode
    {
        public uint DisplayId;
        public uint Format;
        public int Width;
        public int Height;
        public float PixelDensity;
        public float RefreshRate;
        public int RefreshRateNumerator;
        public int RefreshRateDenominator;
        public nint Internal;
    }
}

using System.Runtime.InteropServices;

namespace Capsule.Runtime.Platform;

// Ends the run as a Windows session ends, on a log off, a restart or a shutdown, which SDL3 leaves to the
// default window procedure. Windows ends the process as soon as the window has handled WM_ENDSESSION, and
// the run ends inside it, on this thread, whatever SDL has queued. A no-op off Windows.
internal static unsafe class WindowsSessionEnd
{
    // GWLP_WNDPROC.
    private const int WindowProcedure = -4;

    private const uint EndSessionMessage = 0x0016;

    // SDL's window procedure, which every message goes on to, and what the session's end runs. One window at
    // a time.
    private static nint SdlProcedure;
    private static Action? Ended;

    // Takes SDL's window, not its HWND. ended must not throw.
    internal static void Watch(nint window, Action ended)
    {
        nint handle = OperatingSystem.IsWindows() ? Sdl3.Win32Window(window) : nint.Zero;
        if (handle == nint.Zero)
        {
            return;
        }

        Ended = ended;
        delegate* unmanaged<nint, uint, nuint, nint, nint> procedure = &Procedure;
        SdlProcedure = SetWindowLongPtrW(handle, WindowProcedure, (nint)procedure);
    }

    // Hands the window back to SDL's procedure alone, before SDL destroys it.
    internal static void Stop(nint window)
    {
        nint handle = SdlProcedure == nint.Zero ? nint.Zero : Sdl3.Win32Window(window);
        if (handle == nint.Zero)
        {
            return;
        }

        _ = SetWindowLongPtrW(handle, WindowProcedure, SdlProcedure);
        SdlProcedure = nint.Zero;
        Ended = null;
    }

    // WM_ENDSESSION's wParam is nonzero when the session is ending, and zero when a shutdown was cancelled.
    [UnmanagedCallersOnly]
    private static nint Procedure(nint handle, uint message, nuint wParam, nint lParam)
    {
        if (message == EndSessionMessage && wParam != 0)
        {
            Ended?.Invoke();
            return nint.Zero;
        }

        return CallWindowProcW(SdlProcedure, handle, message, wParam, lParam);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint CallWindowProcW(nint procedure, nint window, uint message, nuint wParam, nint lParam);
}

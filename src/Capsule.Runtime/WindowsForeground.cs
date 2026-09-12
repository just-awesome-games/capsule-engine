using System.Runtime.InteropServices;

namespace Capsule.Runtime;

// Takes the Windows foreground for a window the OS will not hand it to. Windows grants foreground
// activation only along a permission chain — the process that already holds the foreground, or one
// it started — and a launch through the dotnet muxer is outside it, so SDL's raise leaves the window
// behind the terminal and deaf to the keyboard. Attaching to the foreground window's input queue
// borrows its permission for the length of the call, which is the documented way back in. A no-op
// off Windows.
internal static class WindowsForeground
{
    internal static void Claim(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == nint.Zero)
        {
            return;
        }

        nint foreground = GetForegroundWindow();
        if (foreground == window)
        {
            return;
        }

        uint thisThread = GetCurrentThreadId();
        uint foregroundThread = foreground == nint.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        bool attached = foregroundThread != 0
            && foregroundThread != thisThread
            && AttachThreadInput(thisThread, foregroundThread, true);

        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
            SetFocus(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
    }

    // DllImport for the same reason SdlPlatform gives: these marshal ahead of time, and
    // LibraryImport's generated stubs would open the assembly to unsafe code for nothing.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint thread, uint toThread, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
#pragma warning restore SYSLIB1054
}

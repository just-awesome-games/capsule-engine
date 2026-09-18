using System.Runtime.InteropServices;

namespace Capsule.Runtime.Desktop;

// Takes the Windows foreground for a window the OS will not hand it to. Windows grants foreground
// activation only along a permission chain — the process that already holds the foreground, one it
// started, or the one that received the last input event — and a launch through the dotnet muxer
// is outside it, so SDL's raise leaves the window behind the terminal and deaf to the keyboard. A
// synthetic zero-motion mouse move sent from this thread makes this process the last-input process,
// and SetForegroundWindow then takes the ordinary activation path, so the window's own queue becomes
// the foreground queue. Attaching to the foreground thread's input queue is not used: it leaves the
// window active in a queue the foreground state does not know, so the first click elsewhere never
// deactivates it — keys stick, the global mouse reads as the game's, audio never ducks — until a
// click into the window. A no-op off Windows.
internal static class WindowsForeground
{
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;

    internal static void Claim(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == nint.Zero)
        {
            return;
        }

        if (GetForegroundWindow() == window)
        {
            return;
        }

        Input input = default;
        input.Type = InputMouse;
        input.Mouse.Flags = MouseEventMove;
        SendInput(1, ref input, Marshal.SizeOf<Input>());
        SetForegroundWindow(window);
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
    private static extern uint SendInput(uint count, ref Input input, int size);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal int Dx;
        internal int Dy;
        internal uint Data;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    // INPUT. The union's largest member is MOUSEINPUT, so carrying only it gives the native size.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal MouseInput Mouse;
    }
}

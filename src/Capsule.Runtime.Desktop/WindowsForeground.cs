using System.Runtime.InteropServices;

namespace Capsule.Runtime.Desktop;

// Takes the Windows foreground for a window the OS will not hand it to. Windows grants activation
// only to the process that received the last input event, among others. A synthetic zero-motion mouse
// move makes this process that one, and SetForegroundWindow then activates normally. Attaching to the
// foreground thread's input queue is avoided. A window activated that way is never deactivated by a
// click elsewhere. A no-op off Windows.
internal static class WindowsForeground
{
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;

    internal static void Claim(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == nint.Zero || GetForegroundWindow() == window)
        {
            return;
        }

        Input input = default;
        input.Type = InputMouse;
        input.Mouse.Flags = MouseEventMove;
        SendInput(1, ref input, Marshal.SizeOf<Input>());
        SetForegroundWindow(window);
    }

    // DllImport for the reason SdlPlatform gives.
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

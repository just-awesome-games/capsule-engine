using System.Runtime.InteropServices;

namespace Capsule.Runtime.Desktop;

// The scale Windows gives the display a window is on, 1 at 100%. A process that declares no display
// awareness is told 96 dots per inch, a scale of 1, on every display. 1 off Windows.
internal static class WindowsDisplay
{
    private const float UnscaledDpi = 96f;

    internal static float Scale(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == nint.Zero)
        {
            return 1f;
        }

        uint dpi = GetDpiForWindow(window);

        return dpi == 0 ? 1f : dpi / UnscaledDpi;
    }

    // DllImport for the reason SdlPlatform gives.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
#pragma warning restore SYSLIB1054
}

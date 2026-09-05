using System.Reflection;
using System.Runtime.InteropServices;

namespace Capsule.Runtime;

/// <summary>
/// The SDL calls the host makes for itself. The graphics backend keeps its own binding internal
/// and initialises a fixed set of subsystems, so both of these have to be made here; the library
/// is the backend's own, already resident beside the executable.
/// </summary>
internal static class SdlPlatform
{
    private const string LibraryName = "SDL2";

    static SdlPlatform() => NativeLibrary.SetDllImportResolver(typeof(SdlPlatform).Assembly, Resolve);

    /// <summary>
    /// Suppresses SDL's DirectInput backend, which must happen before the backend initialises SDL.
    /// Enumerating it costs about 210 ms of a boot — roughly half inside the joystick subsystem
    /// and the rest inside the haptic subsystem it also backs — and buys only the devices no other
    /// Windows backend reports: XInput, raw input, HID and Windows.Gaming.Input between them cover
    /// the controllers a game meets, and Capsule exposes no rumble for the haptic half to drive.
    /// Set at normal priority, so <c>SDL_DIRECTINPUT_ENABLED=1</c> in the environment still turns
    /// it back on for a device that needs it.
    /// </summary>
    internal static void TrimStartupSubsystems() => SDL_SetHint("SDL_DIRECTINPUT_ENABLED"u8.ToArray(), "0"u8.ToArray());

    /// <summary>
    /// Brings the window to the front and asks for keyboard focus. Windows grants foreground
    /// activation only to a process that already holds it, so a launch from a busy terminal can
    /// still leave the window behind that terminal.
    /// </summary>
    /// <param name="window">The backend's SDL window handle.</param>
    internal static void RaiseWindow(nint window) => SDL_RaiseWindow(window);

    /// <summary>
    /// The default probe derives no candidate that matches the versioned sonames the backend
    /// ships, so the file is named outright, per platform, as the backend's own loader names it.
    /// </summary>
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

    // LibraryImport generates unsafe marshalling stubs, and two blittable calls do not justify
    // opening the whole assembly to unsafe code. Both arguments below are already pointers by the
    // time the marshaller sees them: a pinned byte array is a char*, and the window is an opaque
    // handle.
#pragma warning disable SYSLIB1054
    [DllImport(LibraryName, EntryPoint = "SDL_SetHint")]
    private static extern int SDL_SetHint(byte[] name, byte[] value);

    [DllImport(LibraryName, EntryPoint = "SDL_RaiseWindow")]
    private static extern void SDL_RaiseWindow(nint window);
#pragma warning restore SYSLIB1054
}

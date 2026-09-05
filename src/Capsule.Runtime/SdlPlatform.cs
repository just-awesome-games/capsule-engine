using System.Reflection;
using System.Runtime.InteropServices;

namespace Capsule.Runtime;

// The SDL calls the host makes for itself. The graphics backend keeps its own binding internal and
// initialises a fixed set of subsystems, so both of these have to be made here; the library is the
// backend's own, already resident beside the executable.
internal static class SdlPlatform
{
    private const string LibraryName = "SDL2";

    static SdlPlatform() => NativeLibrary.SetDllImportResolver(typeof(SdlPlatform).Assembly, Resolve);

    // Brings the window to the front and asks for keyboard focus. Windows grants foreground
    // activation only to a process that already holds it, so a launch from a busy terminal can
    // still leave the window behind that terminal.
    internal static void RaiseWindow(nint window) => SDL_RaiseWindow(window);

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

    // LibraryImport generates unsafe marshalling stubs, and one blittable call does not justify
    // opening the whole assembly to unsafe code: the window is an opaque handle, a pointer already.
#pragma warning disable SYSLIB1054
    [DllImport(LibraryName, EntryPoint = "SDL_RaiseWindow")]
    private static extern void SDL_RaiseWindow(nint window);
#pragma warning restore SYSLIB1054
}

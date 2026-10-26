using System.Reflection;
using System.Runtime.InteropServices;

namespace Capsule.Runtime.Desktop;

// The SDL3 calls the desktop platform makes, on the same SDL3 the host runs on: the window's native
// handle and the player's languages, and the audio output's in SdlAudioOutput.
internal static class SdlPlatform
{
    // Every SDL binding in this assembly names this library, which the resolver below maps to SDL3.
    internal const string LibraryName = "SDL3";

    // On Linux the library ships under its versioned soname, which the default probe for the bare
    // name never derives.
    private const string VersionedName = "libSDL3.so.0";

    private const string Win32WindowProperty = "SDL.window.win32.hwnd";

    private static nint Library;

    static SdlPlatform() => NativeLibrary.SetDllImportResolver(typeof(SdlPlatform).Assembly, Resolve);

    // Registers the resolver for the whole assembly, once, in this class's static constructor. A
    // binding outside this class needs it before its own first call.
    internal static void Load()
    {
    }

    // The window's HWND on Windows, or zero elsewhere or when SDL will not report one. SDL's window is
    // its own opaque pointer, not this.
    internal static nint NativeWindowHandle(nint window) =>
        OperatingSystem.IsWindows()
            ? SDL_GetPointerProperty(SDL_GetWindowProperties(window), Win32WindowProperty, nint.Zero)
            : nint.Zero;

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

    // The bare name first, which finds the library beside the executable or under the build's
    // runtimes/ folder on every desktop, then the soname.
    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return nint.Zero;
        }

        if (Library == nint.Zero
            && !NativeLibrary.TryLoad(LibraryName, assembly, searchPath, out Library)
            && !NativeLibrary.TryLoad(VersionedName, assembly, searchPath, out Library))
        {
            Library = nint.Zero;
        }

        return Library;
    }

    // LibraryImport generates unsafe marshalling stubs, and these calls do not justify opening the
    // assembly to unsafe code. A window is an opaque handle, already a pointer.
#pragma warning disable SYSLIB1054
    [DllImport(LibraryName, EntryPoint = "SDL_GetWindowProperties")]
    private static extern uint SDL_GetWindowProperties(nint window);

    [DllImport(LibraryName, EntryPoint = "SDL_GetPointerProperty")]
    private static extern nint SDL_GetPointerProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint defaultValue);

    [DllImport(LibraryName, EntryPoint = "SDL_GetPreferredLocales")]
    private static extern nint SDL_GetPreferredLocales(out int count);

    [DllImport(LibraryName, EntryPoint = "SDL_free")]
    private static extern void SDL_free(nint memory);
#pragma warning restore SYSLIB1054
}

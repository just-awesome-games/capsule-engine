using System.Runtime.InteropServices;

namespace Capsule.Runtime.Platform;

// What the default platform asks SDL of the system: the directory the executable runs from, the
// player's languages and a window's native handle.
internal static unsafe partial class Sdl3
{
    // SDL_PROP_WINDOW_WIN32_HWND_POINTER, null-terminated.
    private static ReadOnlySpan<byte> Win32WindowProperty => "SDL.window.win32.hwnd\0"u8;

    // The executable's directory, with a trailing separator, or a macOS bundle's resources directory.
    // SDL answers before it is initialised.
    internal static string BasePath() =>
        Marshal.PtrToStringUTF8((nint)SDL_GetBasePath())
            ?? throw new InvalidOperationException(
                $"SDL3 reported no directory to read the game's shipped content from ({Error()}). Override HostPlatform.OpenContent to name one.");

    // The operating system's preferred languages as BCP 47 tags, most preferred first. SDL answers
    // before it is initialised, and a failed call yields none.
    internal static IReadOnlyList<string> PreferredLocales()
    {
        int count;
        Locale** locales = SDL_GetPreferredLocales(&count);
        if (locales == null)
        {
            return [];
        }

        // One allocation holds the array of locale pointers and the locales they point to.
        try
        {
            string[] tags = new string[count];
            for (int index = 0; index < count; index++)
            {
                tags[index] = LocaleTag(Marshal.PtrToStringUTF8((nint)locales[index]->Language)!, Marshal.PtrToStringUTF8((nint)locales[index]->Country));
            }

            return tags;
        }
        finally
        {
            SDL_free((nint)locales);
        }
    }

    // One SDL_Locale as a tag: the language alone, or the language and its country.
    internal static string LocaleTag(string language, string? country) =>
        string.IsNullOrEmpty(country) ? language : $"{language}-{country}";

    // The window's HWND, or zero off Windows or when SDL will not report one.
    internal static nint Win32Window(nint window)
    {
        fixed (byte* name = Win32WindowProperty)
        {
            return SDL_GetPointerProperty(SDL_GetWindowProperties(window), name, nint.Zero);
        }
    }

    // SDL owns the string and keeps it for the life of the process.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetBasePath();

    // An array of count locale pointers, in one allocation the caller frees with SDL_free.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern Locale** SDL_GetPreferredLocales(int* count);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint SDL_GetWindowProperties(nint window);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_GetPointerProperty(uint properties, byte* name, nint defaultValue);

    // SDL_Locale. The country is null when the locale names none.
    [StructLayout(LayoutKind.Sequential)]
    internal struct Locale
    {
        public byte* Language;
        public byte* Country;
    }
}

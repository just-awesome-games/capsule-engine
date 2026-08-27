using System.Buffers;
using System.Text;

namespace Capsule.Runtime;

/// <summary>
/// The one place a name that will become a directory or a file is judged portable, for the crash
/// log's folder and for a map's file alike.
/// </summary>
internal static class SafeName
{
    // Fixed rather than Path.GetInvalidFileNameChars(): the POSIX set rejects only '\0'
    // and '/', so a name accepted on a Linux build machine would fail on a player's
    // Windows box. The safe-name contract must not depend on where the game was built.
    private static readonly SearchValues<char> UnsafeNameChars = SearchValues.Create(UnsafeNameCharSet());

    // Windows resolves these as devices from any directory, matching on the stem before
    // the first dot, so "CON" and "CON.log" both fail rather than creating a directory.
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    internal static bool IsOneSafeDirectoryName(string name)
    {
        if (name.AsSpan().IndexOfAny(UnsafeNameChars) >= 0)
        {
            return false;
        }

        // Catches "." and ".." with it: Windows trims trailing dots and spaces, so such a
        // name silently resolves to a different directory than the one it reads as.
        if (name[^1] is '.' or ' ')
        {
            return false;
        }

        ReadOnlySpan<char> stem = name.AsSpan();
        int dot = stem.IndexOf('.');
        if (dot >= 0)
        {
            stem = stem[..dot];
        }

        foreach (string reserved in ReservedDeviceNames)
        {
            if (stem.Equals(reserved, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A display name lowercased, with every run of anything that is not a letter or a digit
    /// becoming one hyphen — so "My Game" is "my-game". Null where what remains is not one safe
    /// directory name, which a game name of punctuation alone or of a reserved device name is.
    /// </summary>
    internal static string? Slug(string name)
    {
        StringBuilder slug = new(name.Length);
        bool separated = false;

        foreach (char character in name)
        {
            if (!char.IsLetterOrDigit(character))
            {
                separated = true;
                continue;
            }

            if (separated && slug.Length > 0)
            {
                slug.Append('-');
            }

            separated = false;
            slug.Append(char.ToLowerInvariant(character));
        }

        if (slug.Length == 0)
        {
            return null;
        }

        string slugged = slug.ToString();

        return IsOneSafeDirectoryName(slugged) ? slugged : null;
    }

    private static char[] UnsafeNameCharSet()
    {
        const string Reserved = "<>:\"/\\|?*";
        const int ControlCharCount = 0x20;

        char[] unsafeChars = new char[Reserved.Length + ControlCharCount];
        Reserved.CopyTo(unsafeChars);
        for (int control = 0; control < ControlCharCount; control++)
        {
            unsafeChars[Reserved.Length + control] = (char)control;
        }

        return unsafeChars;
    }
}

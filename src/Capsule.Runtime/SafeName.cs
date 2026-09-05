using System.Buffers;
using System.Text;
using Capsule.Assets;

namespace Capsule.Runtime;

// Whether a name that will become a directory is portable, which the crash log's folder is the one
// caller of.
internal static class SafeName
{
    // Fixed rather than Path.GetInvalidFileNameChars(): the POSIX set rejects only '\0'
    // and '/', so a name accepted on a Linux build machine would fail on a player's
    // Windows box. The safe-name contract must not depend on where the game was built.
    private static readonly SearchValues<char> UnsafeNameChars = SearchValues.Create(UnsafeNameCharSet());

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

        // Windows matches a device name on the stem before the first dot, so "CON" and "CON.log"
        // both fail rather than creating a directory.
        int dot = name.IndexOf('.', StringComparison.Ordinal);

        return !AssetPaths.IsReservedDeviceName(dot >= 0 ? name[..dot] : name);
    }

    // A display name lowercased, with every run of anything that is not a letter or a digit
    // becoming one hyphen — so "My Game" is "my-game". Null where what remains is not one safe
    // directory name, which a game name of punctuation alone or of a reserved device name is.
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

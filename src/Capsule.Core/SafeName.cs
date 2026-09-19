using System.Buffers;
using System.Text;
using Capsule.Assets;

namespace Capsule;

// Whether a name that will become a file or directory is portable. The runtime's local folder and every
// save document's name pass through here. A name is refused the same way on a headless run with no file
// system and on the player's machine.
internal static class SafeName
{
    // A fixed set, not Path.GetInvalidFileNameChars(). The POSIX set rejects only '\0' and '/', and a name
    // accepted on a Linux build machine would fail on a player's Windows box. The safe-name contract must
    // not depend on where the game was built.
    private static readonly SearchValues<char> UnsafeNameChars = SearchValues.Create(UnsafeNameCharSet());

    internal static bool IsOneSafeDirectoryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.AsSpan().IndexOfAny(UnsafeNameChars) >= 0)
        {
            return false;
        }

        // Windows trims trailing dots and spaces, so such a name silently resolves to a different
        // directory than it reads as. This also catches "." and "..".
        if (name[^1] is '.' or ' ')
        {
            return false;
        }

        // Windows matches a device name on the stem before the first dot, so "CON" and "CON.log"
        // both fail instead of creating a directory.
        int dot = name.IndexOf('.', StringComparison.Ordinal);

        return !AssetPaths.IsReservedDeviceName(dot >= 0 ? name[..dot] : name);
    }

    // A display name lowercased, with each run of non-alphanumeric characters collapsed to one hyphen, so
    // "My Game" becomes "my-game". Returns null when the result is not a safe directory name, as happens
    // for a name of only punctuation or a reserved device name.
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

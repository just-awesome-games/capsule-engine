using System.Text;

namespace Capsule.Assets;

// The two spellings the build's own tree is named by:
//
//   a path is an asset's place under Assets/, extension included: "enemies/bat.png".
//   a key is a document's place under Assets/, without extensions: "scenes/stage-1/room-01".
//
// Neither can reach outside the directory the build owns. This file is also compiled into
// Capsule.Generators, which references no engine assembly, so nothing here may use a type
// netstandard2.0 lacks.
internal static class AssetPaths
{
    // Windows resolves these as devices from any directory, matching on the stem before the first dot, so
    // a directory or file with one of these names does not behave as a file.
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    // A key is the path a file is written at, so its segments admit only the characters a file name can
    // carry on every platform Capsule targets.
    internal static bool IsKey(string key)
    {
        if (key.Length == 0)
        {
            return false;
        }

        int start = 0;
        while (true)
        {
            int slash = key.IndexOf('/', start);
            int end = slash < 0 ? key.Length : slash;

            if (end == start || IsReservedDeviceName(key.Substring(start, end - start)))
            {
                return false;
            }

            for (int i = start; i < end; i++)
            {
                bool safe = key[i] is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '-'
                    or '_';
                if (!safe)
                {
                    return false;
                }
            }

            if (slash < 0)
            {
                return true;
            }

            start = slash + 1;
        }
    }

    internal static bool IsReservedDeviceName(string stem)
    {
        foreach (string reserved in ReservedDeviceNames)
        {
            if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Reduces every '/'-joined segment to the kebab form of the identifier it names, so
    // "Enemies/Bat", "enemies/bat" and "enemies/Bat" are one key. Idempotent, since the kebab form
    // of an identifier names that identifier again. Returns null when a segment names no
    // identifier, and hands that segment back in rejected. The build, the generator and a scene's
    // load all key through this one rule.
    internal static string? NormalizeKey(string key, out string? rejected)
    {
        rejected = null;
        StringBuilder normalized = new(key.Length + 4);
        int start = 0;

        while (true)
        {
            int slash = key.IndexOf('/', start);
            int end = slash < 0 ? key.Length : slash;
            string segment = key.Substring(start, end - start);

            if (ToIdentifier(segment) is not { } identifier)
            {
                rejected = segment;
                return null;
            }

            normalized.Append(FromTypeName(identifier));

            if (slash < 0)
            {
                return normalized.ToString();
            }

            normalized.Append('/');
            start = slash + 1;
        }
    }

    // The key and lower-case extension a document's spelling of an asset names, as "enemies/bat.png",
    // or null when it names none.
    internal static string? NormalizePath(string path) =>
        TrySplit(path, out string name, out string extension) && NormalizeKey(name, out _) is { } key
            ? key + extension.ToLowerInvariant()
            : null;

    internal static string? ToIdentifier(string name)
    {
        StringBuilder identifier = new(name.Length);
        bool startOfWord = true;

        foreach (char character in name)
        {
            if (character is '-' or '_')
            {
                startOfWord = true;
                continue;
            }

            bool legal = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
            if (!legal)
            {
                return null;
            }

            identifier.Append(startOfWord ? char.ToUpperInvariant(character) : character);
            startOfWord = false;
        }

        return identifier.Length > 0 && !char.IsDigit(identifier[0]) ? identifier.ToString() : null;
    }

    internal static string FromTypeName(string typeName)
    {
        StringBuilder id = new(typeName.Length + 4);

        for (int i = 0; i < typeName.Length; i++)
        {
            char character = typeName[i];

            if (char.IsDigit(character))
            {
                if (i > 0 && char.IsLetter(typeName[i - 1]))
                {
                    id.Append('-');
                }

                id.Append(character);
                continue;
            }

            if (!char.IsUpper(character))
            {
                id.Append(character);
                continue;
            }

            bool startsWord = i > 0
                && (!char.IsUpper(typeName[i - 1]) || (i + 1 < typeName.Length && char.IsLower(typeName[i + 1])));
            if (startsWord)
            {
                id.Append('-');
            }

            id.Append(char.ToLowerInvariant(character));
        }

        return id.ToString();
    }

    // Split on the last dot, not matched against known extensions. Which extensions a domain admits is
    // the build's allow-list.
    internal static bool TrySplit(string path, out string name, out string extension)
    {
        name = string.Empty;
        extension = string.Empty;

        if (!IsPath(path))
        {
            return false;
        }

        int dot = path.LastIndexOf('.');
        int lastSegment = path.LastIndexOf('/') + 1;
        if (dot <= lastSegment || dot == path.Length - 1)
        {
            return false;
        }

        name = path.Substring(0, dot);
        extension = path.Substring(dot);

        return true;
    }

    // The inverse of the split. A written name gives its handle back unchanged. A name carrying dots
    // is fine, because "x.atlas" and ".png" split apart again at the last dot.
    internal static bool Joins(string name, string extension) =>
        extension is { Length: > 1 }
        && extension[0] == '.'
        && extension.IndexOf('.', 1) < 0
        && extension.IndexOf('/') < 0
        && extension.IndexOf('\\') < 0
        && IsPath(name)
        && name.LastIndexOf('/') < name.Length - 1;

    // Forward slashes only, and every segment must name something. A backslash, an empty segment, or a
    // '.' or '..' segment could reach outside the directory the build ships into.
    private static bool IsPath(string value)
    {
        if (value.Length == 0 || value.IndexOf('\\') >= 0)
        {
            return false;
        }

        int start = 0;
        while (true)
        {
            int slash = value.IndexOf('/', start);
            int end = slash < 0 ? value.Length : slash;
            int length = end - start;

            if (length == 0
                || (length == 1 && value[start] == '.')
                || (length == 2 && value[start] == '.' && value[start + 1] == '.'))
            {
                return false;
            }

            if (slash < 0)
            {
                return true;
            }

            start = slash + 1;
        }
    }
}

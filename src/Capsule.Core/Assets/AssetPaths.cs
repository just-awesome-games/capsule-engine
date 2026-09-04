namespace Capsule.Assets;

// The two spellings the build's own tree is named by, in one place so nothing accepts what
// something else would reject:
//
//   a path — an asset's place under its domain root, extension included: "enemies/bat.png", or
//   "tiles.png" for a file at the root, which is how a document names a texture;
//
//   a key — a document's place under its root, without extensions: "stage-1/room-01", which is
//   what a scene or sheet source claims and what its derived file is written at.
//
// Neither can reach outside the directory the build owns. Capsule.Generators holds the only other
// copy of the key rule, since a source generator references no engine assembly.
internal static class AssetPaths
{
    // Windows resolves these as devices from any directory, matching on the stem before the first
    // dot, so a directory or file of one of these names is not a file at all.
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    // A key is the path a file is written at, so its segments are the characters a file name
    // carries on every platform Capsule targets and nothing else.
    internal static bool IsKey(string key)
    {
        if (key.Length == 0)
        {
            return false;
        }

        int start = 0;
        while (start <= key.Length)
        {
            int slash = key.IndexOf('/', start);
            int end = slash < 0 ? key.Length : slash;

            if (end == start || IsReservedDeviceName(key.AsSpan(start, end - start)))
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

        return true;
    }

    internal static bool IsReservedDeviceName(ReadOnlySpan<char> stem)
    {
        foreach (string reserved in ReservedDeviceNames)
        {
            if (stem.Equals(reserved, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Split on the last dot rather than matched against a known extension: the handle carries
    // whatever the build shipped, and which extensions a domain admits is the build's allow-list.
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

        name = path[..dot];
        extension = path[dot..];

        return true;
    }

    // The exact inverse of the split, which is what makes a written name give its handle back
    // unchanged. A name carrying dots of its own is fine: "x.atlas" and ".png" write "x.atlas.png"
    // and split apart again at the last one.
    internal static bool Joins(string name, string extension) =>
        extension is { Length: > 1 }
        && extension[0] == '.'
        && extension.IndexOf('.', 1) < 0
        && extension.IndexOf('/') < 0
        && extension.IndexOf('\\') < 0
        && IsPath(name)
        && name.LastIndexOf('/') < name.Length - 1;

    // Forward slashes only, and every segment names something: a backslash, an empty segment, or a
    // '.' or '..' segment would reach outside the directory the build ships into, or name nothing
    // at all.
    private static bool IsPath(string value)
    {
        if (value.Length == 0 || value.IndexOf('\\') >= 0)
        {
            return false;
        }

        int start = 0;
        while (start <= value.Length)
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

        return true;
    }
}

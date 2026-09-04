namespace Capsule.Assets;

// The two spellings the build's own tree is named by:
//
//   a path — an asset's place under its domain root, extension included: "enemies/bat.png";
//   a key — a document's place under its root, without extensions: "stage-1/room-01".
//
// Neither can reach outside the directory the build owns. Compiled into Capsule.Generators as well,
// which references no engine assembly, so nothing here may use a type netstandard2.0 lacks.
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

    // A key is the path a file is written at, so its segments carry the characters a file name
    // carries on every platform Capsule targets and nothing else.
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

    // Split on the last dot rather than matched against a known extension: which extensions a
    // domain admits is the build's allow-list, not this rule's.
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

    // The exact inverse of the split, so a written name gives its handle back unchanged. A name
    // carrying dots of its own is fine: "x.atlas" and ".png" split apart again at the last one.
    internal static bool Joins(string name, string extension) =>
        extension is { Length: > 1 }
        && extension[0] == '.'
        && extension.IndexOf('.', 1) < 0
        && extension.IndexOf('/') < 0
        && extension.IndexOf('\\') < 0
        && IsPath(name)
        && name.LastIndexOf('/') < name.Length - 1;

    // Forward slashes only, and every segment names something: a backslash, an empty segment, or a
    // '.' or '..' segment would reach outside the directory the build ships into.
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

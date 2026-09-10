using Capsule.Assets;

namespace Capsule.Runtime.Assets;

// Where a named asset's file is, as a path and nothing else. One instance per shipped domain, so
// resolution and its failure are testable without a device.
internal sealed class AssetFiles(string domain, string noun, string parameterName)
{
    // The asset's file, relative to the executable. A name is its source's path under the domain
    // root, so a nested asset resolves to a nested file.
    internal string RelativePathOf(string? name, string? extension)
    {
        Validate(name, extension);

        return "assets/" + domain + "/" + name + extension;
    }

    internal string Locate(string baseDirectory, string? name, string? extension)
    {
        string relative = RelativePathOf(name, extension);
        string root = Path.GetFullPath(Path.Combine(baseDirectory, "assets", domain));
        string path = Path.GetFullPath(Path.Combine(root, name + extension));
        string containedBy = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!path.StartsWith(containedBy, comparison))
        {
            throw Invalid(name, extension);
        }

        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                $"{noun} '{name}' has no shipped file at '{relative}' beside the executable.",
                path);
    }

    private void Validate(string? name, string? extension)
    {
        if (name is null || extension is null || !AssetPaths.Joins(name, extension))
        {
            throw Invalid(name, extension);
        }

        int start = 0;
        while (true)
        {
            int slash = name.IndexOf('/', start);
            if (slash < 0)
            {
                if (!SafeName.IsOneSafeDirectoryName(name[start..] + extension))
                {
                    throw Invalid(name, extension);
                }

                return;
            }

            if (!SafeName.IsOneSafeDirectoryName(name[start..slash]))
            {
                throw Invalid(name, extension);
            }

            start = slash + 1;
        }
    }

    private ArgumentException Invalid(string? name, string? extension) =>
        new(
            $"{noun} handle ('{name}', '{extension}') does not name one portable file under assets/{domain}.",
            parameterName);
}

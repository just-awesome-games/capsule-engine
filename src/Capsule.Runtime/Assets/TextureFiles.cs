using Capsule.Assets;

namespace Capsule.Runtime.Assets;

// Where a texture handle's file is, as a path and nothing else. Separate from the store so the
// resolution and its failure are testable without a graphics device.
internal static class TextureFiles
{
    private const string DomainDirectory = "textures";

    // The handle's file, relative to the executable. A handle's name is its source's path under the
    // textures root, so a nested asset resolves to a nested file.
    internal static string RelativePathOf(in TextureHandle handle)
    {
        Validate(handle);

        return "assets/" + DomainDirectory + "/" + handle.Name + handle.Extension;
    }

    internal static string Locate(string baseDirectory, in TextureHandle handle)
    {
        string relative = RelativePathOf(handle);
        string root = Path.GetFullPath(Path.Combine(baseDirectory, "assets", DomainDirectory));
        string path = Path.GetFullPath(Path.Combine(root, handle.Name + handle.Extension));
        string containedBy = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!path.StartsWith(containedBy, comparison))
        {
            throw Invalid(handle);
        }

        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                $"Texture '{handle.Name}' has no shipped file at '{relative}' beside the executable.",
                path);
    }

    private static void Validate(in TextureHandle handle)
    {
        if (handle.Name is not { } name
            || handle.Extension is not { } extension
            || !AssetPaths.Joins(name, extension))
        {
            throw Invalid(handle);
        }

        int start = 0;
        while (true)
        {
            int slash = name.IndexOf('/', start);
            if (slash < 0)
            {
                if (!SafeName.IsOneSafeDirectoryName(name[start..] + extension))
                {
                    throw Invalid(handle);
                }

                return;
            }

            if (!SafeName.IsOneSafeDirectoryName(name[start..slash]))
            {
                throw Invalid(handle);
            }

            start = slash + 1;
        }
    }

    private static ArgumentException Invalid(in TextureHandle handle) =>
        new(
            $"Texture handle ('{handle.Name}', '{handle.Extension}') does not name one portable file under assets/textures.",
            nameof(handle));
}

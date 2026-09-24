using Capsule.Assets;

namespace Capsule.Runtime.Assets;

// Where a named asset's file is, as a content path. One instance per asset type, so resolution
// and its failure are testable without a device.
internal sealed class AssetFiles(string noun, string parameterName)
{
    // The asset's file, relative to the publish root. A name is its source's path under Assets/, and
    // a nested asset resolves to a nested file.
    internal string RelativePathOf(string? name, string? extension)
    {
        Validate(name, extension);

        return "assets/" + name + extension;
    }

    // Opens the asset's shipped file through the platform. The caller disposes the stream.
    internal Stream Open(HostPlatform platform, string? name, string? extension)
    {
        string relative = RelativePathOf(name, extension);

        try
        {
            return platform.OpenContent(relative);
        }
        catch (IOException missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileNotFoundException(
                $"{noun} '{name}' has no shipped file at '{relative}'.",
                relative,
                missing);
        }
    }

    // Every segment must be one safe directory name, which keeps a name inside assets/. No
    // separator but '/', no '.' or '..', no rooted or device path.
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
            $"{noun} handle ('{name}', '{extension}') does not name one portable file under assets/.",
            parameterName);
}

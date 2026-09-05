using Capsule.Assets;

namespace Capsule.Runtime.Rendering;

// Where a texture handle's file is, as a path and nothing else. Separate from the store so the
// resolution and its failure are testable without a graphics device.
internal static class TextureFiles
{
    private const string DomainDirectory = "textures";

    // The handle's file, relative to the executable. A handle's name is its source's path under the
    // textures root, so a nested asset resolves to a nested file.
    internal static string RelativePathOf(in TextureHandle handle) =>
        "assets/" + DomainDirectory + "/" + handle.Name + handle.Extension;

    // Every handle's file under baseDirectory, in first-appearance order, with a handle named more
    // than once resolved once.
    internal static (TextureHandle Handle, string Path)[] Resolve(
        string baseDirectory,
        IReadOnlyList<TextureHandle> handles)
    {
        List<(TextureHandle Handle, string Path)> resolved = new(handles.Count);
        HashSet<TextureHandle> seen = new(handles.Count);

        foreach (TextureHandle handle in handles)
        {
            if (seen.Add(handle))
            {
                resolved.Add((handle, Locate(baseDirectory, handle)));
            }
        }

        return [.. resolved];
    }

    internal static string Locate(string baseDirectory, in TextureHandle handle)
    {
        string relative = RelativePathOf(handle);
        string path = Path.Combine(baseDirectory, relative);

        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                $"Texture '{handle.Name}' is registered by the build but ships no file: nothing is at '{relative}' beside the executable.",
                path);
    }
}

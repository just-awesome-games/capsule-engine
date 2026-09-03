using Capsule.Assets;

namespace Capsule.Runtime.Rendering;

// Where a texture handle's file is, as a path and nothing else. Separate from the store so the
// resolution and its failure are testable without a graphics device.
internal static class TextureFiles
{
    private const string DomainDirectory = "textures";

    /// <summary>The handle's file, relative to the directory the executable ships its assets in.</summary>
    internal static string RelativePathOf(in TextureHandle handle) =>
        Path.Combine("assets", DomainDirectory, handle.Name + handle.Extension).Replace('\\', '/');

    /// <summary>
    /// Every handle's file under <paramref name="baseDirectory"/>, in first-appearance order, with
    /// a handle named more than once resolved once. Registries aggregate per logic assembly and two
    /// of them may ship under one stem, which names one file and is one texture.
    /// </summary>
    /// <exception cref="FileNotFoundException">A handle's file is not there; nothing is returned.</exception>
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

    /// <summary>The handle's file under <paramref name="baseDirectory"/>.</summary>
    /// <exception cref="FileNotFoundException">Nothing is shipped there.</exception>
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

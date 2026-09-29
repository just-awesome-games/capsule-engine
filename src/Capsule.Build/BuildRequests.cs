using System.IO.Enumeration;
using Capsule.Build.Shaders;

namespace Capsule.Build;

/// <summary>
/// What one run builds: every file under the asset root, walked by the run itself, and the options the
/// targets passed on its command line.
/// </summary>
/// <param name="AssetRoot">The authoring tree, <c>Assets/</c>, relative to the working directory.</param>
/// <param name="ShaderTools">The shader tools the build restored.</param>
/// <param name="Shipping">Whether the build is a publish, which leaves out every development-only source.</param>
/// <param name="Sources">Every file under the asset root in ordinal path order, then every file an importer wrote.</param>
internal sealed record BuildRequests(string AssetRoot, ShaderTools ShaderTools, bool Shipping, IReadOnlyList<Request> Sources)
{
    // Every file, hidden and system files included.
    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    /// <summary>Walks every file under <paramref name="assetRoot"/>, which may not exist.</summary>
    internal static BuildRequests Walk(string assetRoot, ShaderTools shaderTools, bool shipping)
    {
        string root = Relative(assetRoot);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetRoot));
        List<Request> sources = [];
        if (Directory.Exists(full))
        {
            // Each path is the root as the working directory reaches it, then the path below it as the disk spells it.
            FileSystemEnumerable<Request> files = new(
                full,
                (ref FileSystemEntry entry) => new Request(
                    string.Concat(root, entry.Directory[full.Length..].ToString().Replace('\\', '/'), "/", entry.FileName),
                    entry.Length,
                    entry.LastWriteTimeUtc.UtcDateTime),
                Everything)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory,
            };
            sources.AddRange(files);
            sources.Sort(static (first, second) => string.CompareOrdinal(first.Path, second.Path));
        }

        return new BuildRequests(root, shaderTools, shipping, sources);
    }

    // Every path a message names is relative to the project, the working directory, so a build log
    // and a document's provenance carry no machine's own layout.
    internal static string Relative(string path) =>
        System.IO.Path.GetRelativePath(Environment.CurrentDirectory, path).Replace('\\', '/');
}

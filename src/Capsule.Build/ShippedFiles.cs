namespace Capsule.Build;

/// <summary>
/// Everything the game ships, laid out under one directory exactly as it lands beside the executable.
/// The targets ship that directory whole. A run claims every file it ships, and what it did not claim
/// is left over from an earlier run and deleted.
/// </summary>
internal sealed class ShippedFiles(string root)
{
    // Each claimed file and what claimed it.
    private readonly Dictionary<string, string> _claimed = new(Keys.PathComparer);

    /// <summary>The directory everything ships under, the shipped <c>assets/</c>.</summary>
    internal string Root { get; } = root;

    /// <summary>The shipped path's file on disk, created directory included, claimed for this run.</summary>
    /// <param name="shipped">The path below <c>assets/</c>, as <c>textures/enemies/bat.png</c>.</param>
    /// <param name="owner">What ships there, as a refusal names it. One owner may claim a path again.</param>
    /// <exception cref="FormatException">Another owner already ships at the path.</exception>
    internal string Claim(string shipped, string owner)
    {
        string path = Path.GetFullPath(Path.Combine(Root, shipped));
        if (_claimed.TryGetValue(path, out string? claimant) && claimant != owner)
        {
            throw new FormatException($"ships at \"assets/{shipped}\", where {claimant} already ships. Rename it.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _claimed[path] = owner;

        return path;
    }

    /// <summary>Ships <paramref name="source"/> as it was authored, copying only when it changed.</summary>
    internal void Copy(string source, string shipped)
    {
        string path = Claim(shipped, $"'{source}'");
        FileInfo from = new(source);
        FileInfo to = new(path);

        // The size and write-time check MSBuild's own unchanged-file copy makes.
        if (!to.Exists || to.Length != from.Length || to.LastWriteTimeUtc != from.LastWriteTimeUtc)
        {
            from.CopyTo(path, overwrite: true);
            File.SetLastWriteTimeUtc(path, from.LastWriteTimeUtc);
        }
    }

    /// <summary>Deletes every file under the root this run did not claim.</summary>
    internal void Prune()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            if (!_claimed.ContainsKey(Path.GetFullPath(file)))
            {
                File.Delete(file);
            }
        }
    }
}

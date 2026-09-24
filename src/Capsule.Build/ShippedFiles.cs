namespace Capsule.Build;

/// <summary>
/// Everything the game ships, laid out under one directory exactly as it lands beside the executable.
/// The targets ship that directory whole. A run claims every file it ships, and what it did not claim
/// is left over from an earlier run and deleted.
/// </summary>
internal sealed class ShippedFiles(string root)
{
    // The host's own case rule, as the targets compare paths.
    private readonly HashSet<string> _claimed = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    /// <summary>The shipped path's file on disk, created directory included, claimed for this run.</summary>
    /// <param name="shipped">The path below <c>assets/</c>, as <c>textures/enemies/bat.png</c>.</param>
    internal string Claim(string shipped)
    {
        string path = Path.GetFullPath(Path.Combine(root, shipped));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _claimed.Add(path);

        return path;
    }

    /// <summary>Ships <paramref name="source"/> as it was authored, copying only when it changed.</summary>
    internal void Copy(string source, string shipped)
    {
        string path = Claim(shipped);
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
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!_claimed.Contains(Path.GetFullPath(file)))
            {
                File.Delete(file);
            }
        }
    }
}

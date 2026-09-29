namespace Capsule.Build.Caching;

/// <summary>
/// The files one derivation reads beyond the inputs its description names, and the files it writes,
/// each recorded in its cache entry.
/// </summary>
/// <param name="outputs">Where it writes: the shipped <c>assets/</c>, or <c>imported/</c> for an import.</param>
/// <param name="owner">What writes them, as a collision names it.</param>
internal sealed class DerivedFiles(OutputFiles outputs, string owner)
{
    private readonly List<(string Path, long Length, DateTime Written)> _written = [];

    private readonly List<string> _read = [];

    /// <summary>Every file the derivation wrote, by its path below its root, as each lands there.</summary>
    internal IReadOnlyList<(string Path, long Length, DateTime Written)> Written => _written;

    /// <summary>Every file the derivation read that its description could not name, relative to the working directory, in the order first read.</summary>
    internal IReadOnlyList<string> Read => _read;

    /// <summary>Records a file the derivation reads, which the cache stamps beside its named inputs.</summary>
    /// <param name="path">The file relative to the working directory, or absolute.</param>
    /// <returns>The path relative to the working directory, as the walk spells it.</returns>
    internal string Reads(string path)
    {
        string relative = BuildRequests.Relative(path);
        if (!_read.Contains(relative, Keys.PathComparer))
        {
            _read.Add(relative);
        }

        return relative;
    }

    /// <summary>Claims the file at <paramref name="path"/> below the root and writes it.</summary>
    /// <param name="path">The path below the root, as <c>textures/enemies/bat.png</c>.</param>
    /// <param name="write">Writes the whole file at the path it is handed.</param>
    /// <returns>The record <see cref="Written"/> gains. A path another owner claimed first is not written, and records no length.</returns>
    internal (string Path, long Length, DateTime Written) Write(string path, Action<string> write)
    {
        // Two writers moving onto one file at once can fail either. The later claimant only records
        // the path, and the collision fails both.
        (string, long, DateTime) record = (path, 0, default);
        if (outputs.Claim(path, owner))
        {
            string target = Path.Combine(outputs.Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            AtomicFile.Write(target, write);
            FileInfo landed = new(target);
            record = (path, landed.Length, landed.LastWriteTimeUtc);
        }

        _written.Add(record);

        return record;
    }

    /// <summary>Ships <paramref name="source"/> at <paramref name="path"/> as it was authored.</summary>
    internal void Copy(string source, string path) => Write(path, temporary => File.Copy(source, temporary));
}

namespace Capsule.Build;

/// <summary>
/// The files a run writes under one directory. A run claims every file it writes or keeps there, and
/// what it did not claim is left over from an earlier run and deleted.
/// </summary>
/// <remarks>
/// <c>assets/</c> holds everything the game ships, laid out exactly as it lands beside the executable,
/// and the targets ship it whole. <c>imported/</c> holds what the importers wrote. Every member but
/// <see cref="Prune"/> is safe to call concurrently.
/// </remarks>
internal sealed class OutputFiles(string root)
{
    // Each claimed path below the root, and every owner that claimed it in the order claimed. A claim
    // refuses nothing. The run fails every owner of a path claimed twice and deletes the file there. A
    // failed derivation keeps no cache entry, and reuse checks each output's length and write time. A
    // file one owner clobbered is never vouched for by another.
    private readonly Dictionary<string, List<string>> _claimed = new(Keys.PathComparer);

    // Each file under the root before the run claimed any, by its path below the root. Reusing a
    // derivation and pruning share this one directory walk.
    private readonly Lazy<Dictionary<string, (long Length, DateTime Written)>> _found = new(() => Found(root));

    /// <summary>The directory every file is written under, as <c>obj/capsule/assets</c>.</summary>
    internal string Root { get; } = root;

    /// <summary>Claims <paramref name="path"/> for <paramref name="owner"/> this run.</summary>
    /// <param name="path">The path below the root, as <c>textures/enemies/bat.png</c>.</param>
    /// <param name="owner">What writes there, as <see cref="Owners"/> names it. One owner may claim a path again.</param>
    /// <returns>Whether no other owner claimed the path first. Only that owner writes the file.</returns>
    internal bool Claim(string path, string owner)
    {
        // The files found are those from before the run's first claim.
        _ = _found.Value;
        lock (_claimed)
        {
            if (!_claimed.TryGetValue(path, out List<string>? owners))
            {
                _claimed.Add(path, [owner]);

                return true;
            }

            if (!owners.Contains(owner))
            {
                owners.Add(owner);
            }

            return owners[0] == owner;
        }
    }

    /// <summary>Every owner that claimed <paramref name="path"/>, more than one when owners collide there.</summary>
    internal string[] Owners(string path)
    {
        lock (_claimed)
        {
            return _claimed.TryGetValue(path, out List<string>? owners) ? [.. owners] : [];
        }
    }

    /// <summary>Whether a file was written at <paramref name="path"/> before this run, of the length and write time given.</summary>
    internal bool Holds(string path, long length, DateTime written) =>
        _found.Value.TryGetValue(path, out (long Length, DateTime Written) found) && found == (length, written);

    /// <summary>Deletes every file under the root this run did not claim.</summary>
    internal void Prune()
    {
        foreach (string path in _found.Value.Keys)
        {
            if (!_claimed.ContainsKey(path))
            {
                File.Delete(Path.Combine(Root, path));
            }
        }
    }

    private static Dictionary<string, (long Length, DateTime Written)> Found(string root)
    {
        Dictionary<string, (long Length, DateTime Written)> found = new(Keys.PathComparer);
        if (Directory.Exists(root))
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            foreach (FileInfo file in new DirectoryInfo(full).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                found[file.FullName[(full.Length + 1)..].Replace('\\', '/')] = (file.Length, file.LastWriteTimeUtc);
            }
        }

        return found;
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Capsule.Build.Caching;

/// <summary>
/// The SHA-256 of each file a derivation reads, hashed at most once per run. A file whose length and
/// write time match the last run's record reuses that record's hash without being read. Derivations
/// read hashes concurrently. Only <see cref="Found"/> runs alone.
/// </summary>
internal sealed class ContentHashes
{
    private readonly IReadOnlyDictionary<string, FileRecordJson> _previous;

    private readonly DateTime _started;

    private readonly Dictionary<string, Request> _walked = new(Keys.PathComparer);

    private readonly ConcurrentDictionary<string, FileRecordJson> _hashed = new(Keys.PathComparer);

    /// <param name="previous">The last run's records by path.</param>
    /// <param name="started">When the last run started, on the output volume's clock, UTC.</param>
    /// <param name="walked">Every file this run found, with the length and write time it found.</param>
    internal ContentHashes(IReadOnlyDictionary<string, FileRecordJson> previous, DateTime started, IEnumerable<Request> walked)
    {
        _previous = previous;
        _started = started;
        foreach (Request request in walked)
        {
            _walked[request.Path] = request;
        }
    }

    /// <summary>Every file hashed this run in ordinal path order, each with the length and write time it was hashed at.</summary>
    internal IEnumerable<KeyValuePair<string, FileRecordJson>> Hashed =>
        _hashed.OrderBy(static file => file.Key, StringComparer.Ordinal);

    /// <summary>Adds files the run wrote after its walk, each with the hash of the bytes it wrote.</summary>
    internal void Found(IEnumerable<Request> written)
    {
        foreach (Request request in written)
        {
            _walked[request.Path] = request;
        }
    }

    /// <summary>The hash <see cref="Of"/> reads, or null when no file is at <paramref name="path"/>.</summary>
    internal string? OfPresent(string path) =>
        _hashed.ContainsKey(path) || _walked.ContainsKey(path) || File.Exists(path) ? Of(path) : null;

    /// <summary>The lower-case hex SHA-256 of the file at <paramref name="path"/>.</summary>
    internal string Of(string path)
    {
        if (_hashed.TryGetValue(path, out FileRecordJson? known))
        {
            return known.Sha256!;
        }

        if (!_walked.TryGetValue(path, out Request found))
        {
            FileInfo file = new(path);
            found = new Request(path, file.Length, file.LastWriteTimeUtc);
        }

        // The last run took every hash after its start. An edit that kept a file's length and write time
        // came before that start only when the write time is strictly earlier than the start's tick.
        FileRecordJson record = _previous.TryGetValue(path, out FileRecordJson? previous)
            && previous.Length == found.Length && previous.Written == found.Written && found.Written < _started
                ? previous
                : new FileRecordJson { Length = found.Length, Written = found.Written, Sha256 = found.Sha256 ?? Hash(path) };

        // Two derivations reading one file may both hash it. Both hashes are the same.
        return _hashed.GetOrAdd(path, record).Sha256!;
    }

    private static string Hash(string path)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);

        return Convert.ToHexStringLower(SHA256.HashData(file));
    }
}

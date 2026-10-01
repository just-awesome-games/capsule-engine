using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Capsule.Build.Caching;

/// <summary>
/// Every derivation the last run completed, read from <see cref="FileName"/> under the output
/// directory, and every one this run completes, written back when it ends. A derivation is reused when
/// its stamp matches its entry and every file the entry wrote is still there at the length and write
/// time it recorded. Otherwise it runs, and its entry is replaced.
/// </summary>
/// <remarks>
/// <para>
/// The file holds exactly the derivations the run that wrote it completed. It is written after every
/// file it vouches for, and a derivation that failed leaves no entry. A stale entry never skips the
/// work that would restore its files.
/// </para>
/// <para>
/// A derivation may read files its description cannot name before it runs, as an import reads the
/// files its source names. Its entry records them. The next run stamps it over the files that entry
/// recorded, and a file deleted since never matches.
/// </para>
/// </remarks>
internal sealed class DerivationCache
{
    internal const string FileName = "derivation-cache.json";

    private readonly string _path;

    private readonly DateTime _started;

    private readonly int _parallelism;

    private readonly ToolBuilds _tools;

    private readonly Dictionary<string, Dictionary<string, DerivationJson>> _previous = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Dictionary<string, DerivationJson>> _completed = new(StringComparer.Ordinal);

    /// <summary>The hash of every file a derivation reads.</summary>
    internal ContentHashes Hashes { get; }

    private DerivationCache(string path, DateTime started, CapsuleBuild configuration, ContentHashes hashes)
    {
        _path = path;
        _started = started;
        _parallelism = configuration.Parallelism;
        _tools = new ToolBuilds(configuration.IdentifyTool);
        Hashes = hashes;
    }

    /// <summary>Reads the cache under <paramref name="outputDirectory"/>. A missing file, or one this build cannot read, is an empty cache.</summary>
    /// <param name="started">When this run started, on the output volume's clock, UTC. It took no hash before then.</param>
    /// <param name="requests">Every file this run found.</param>
    internal static DerivationCache Read(string outputDirectory, DateTime started, IEnumerable<Request> requests, CapsuleBuild configuration)
    {
        string path = Path.Combine(outputDirectory, FileName);
        DerivationCacheJson? read = null;
        try
        {
            using FileStream file = File.OpenRead(path);
            read = JsonSerializer.Deserialize(file, DerivationCacheJsonContext.Default.DerivationCacheJson);
        }
        catch (Exception ex) when (ex is JsonException || PipelinePass.IsReportable(ex))
        {
            // The build's own cache. A file it cannot read is no cache, and every derivation runs.
        }

        read ??= new DerivationCacheJson();

        Dictionary<string, FileRecordJson> files = new(Keys.PathComparer);
        foreach ((string file, FileRecordJson? record) in read.Files)
        {
            if (record?.Sha256 is not null)
            {
                files.TryAdd(file, record);
            }
        }

        DerivationCache cache = new(path, started, configuration, new ContentHashes(files, read.Started, requests));
        foreach ((string step, Dictionary<string, DerivationJson>? entries) in read.Derivations)
        {
            Dictionary<string, DerivationJson> byName = new(Keys.PathComparer);
            foreach ((string name, DerivationJson? entry) in entries ?? [])
            {
                if (entry is not null)
                {
                    byName.TryAdd(name, entry);
                }
            }

            cache._previous[step] = byName;
        }

        return cache;
    }

    /// <summary>
    /// A SHA-256 over <paramref name="step"/>, the build of the derivation's tool, its settings, and
    /// the path and content hash of each input and then of each file in <paramref name="read"/>.
    /// </summary>
    /// <param name="read">The files the derivation read beyond its inputs. One that is gone hashes as no content.</param>
    internal string Stamp(string step, Derivation derivation, IReadOnlyList<string> read)
    {
        using IncrementalHash stamp = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(stamp, step);
        Add(stamp, _tools.Of(derivation.Tool));
        Add(stamp, derivation.Settings);
        IReadOnlyList<string> inputs = derivation.Inputs;
        string[] hashes = new string[inputs.Count];
        Concurrently.For(hashes.Length, _parallelism, i => hashes[i] = Hashes.Of(inputs[i]));
        for (int i = 0; i < hashes.Length; i++)
        {
            Add(stamp, inputs[i]);
            Add(stamp, hashes[i]);
        }

        foreach (string file in read)
        {
            Add(stamp, file);
            Add(stamp, Hashes.OfPresent(file) ?? string.Empty);
        }

        return Convert.ToHexStringLower(stamp.GetHashAndReset());
    }

    /// <summary>
    /// The derivation's entry from the last run, when its stamp still matches and every file it wrote
    /// under <paramref name="written"/> is still there as it recorded. Safe to call concurrently.
    /// </summary>
    /// <param name="facts">How the entry's facts read, or null when the step keeps none.</param>
    /// <param name="value">The entry's facts, or the default when the step keeps none.</param>
    /// <returns>The entry to hand <see cref="Complete"/> once its files are claimed, or null when the derivation runs again.</returns>
    internal DerivationJson? Reusable<T>(string step, Derivation derivation, OutputFiles written, JsonTypeInfo<T>? facts, out T value)
    {
        value = default!;
        if (!_previous.TryGetValue(step, out Dictionary<string, DerivationJson>? entries)
            || !entries.TryGetValue(derivation.Name, out DerivationJson? entry)
            || entry.Stamp != Stamp(step, derivation, entry.Read ?? [])
            || entry.Outputs.Any(output => !written.Holds(output.Key, output.Value.Length, output.Value.Written)))
        {
            return null;
        }

        if (facts is not null)
        {
            try
            {
                if (entry.Facts is not { } element || element.Deserialize(facts) is not { } read)
                {
                    return null;
                }

                value = read;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return entry;
    }

    /// <summary>The entry for a derivation that just ran and succeeded. Safe to call concurrently.</summary>
    internal DerivationJson Entry<T>(string step, Derivation derivation, DerivedFiles files, T value, JsonTypeInfo<T>? facts)
    {
        string[] read = [.. files.Read.Where(file => !derivation.Inputs.Contains(file, Keys.PathComparer))];

        return new DerivationJson
        {
            Stamp = Stamp(step, derivation, read),
            Tool = derivation.Tool.GetName().Name!,
            Settings = derivation.Settings,
            Inputs = [.. derivation.Inputs],
            Read = read.Length == 0 ? null : read,
            Outputs = files.Written.ToDictionary(
                static file => file.Path,
                static file => new FileRecordJson { Length = file.Length, Written = file.Written },
                StringComparer.Ordinal),
            Facts = facts is null ? null : JsonSerializer.SerializeToElement(value, facts),
        };
    }

    /// <summary>Records <paramref name="entry"/> as completed this run, replacing any entry the derivation had.</summary>
    internal void Complete(string step, string name, DerivationJson entry)
    {
        if (!_completed.TryGetValue(step, out Dictionary<string, DerivationJson>? entries))
        {
            entries = new Dictionary<string, DerivationJson>(Keys.PathComparer);
            _completed.Add(step, entries);
        }

        entries[name] = entry;
    }

    /// <summary>Writes the cache through a temporary beside it, holding exactly the derivations this run completed.</summary>
    internal void Write()
    {
        DerivationCacheJson written = new()
        {
            Started = _started,
            Files = Hashes.Hashed.ToDictionary(static file => file.Key, static file => file.Value, StringComparer.Ordinal),
            Derivations = _completed,
        };

        string temporary = _path + ".tmp";
        using (FileStream file = File.Create(temporary))
        {
            JsonSerializer.Serialize(file, written, DerivationCacheJsonContext.Default.DerivationCacheJson);
        }

        File.Move(temporary, _path, overwrite: true);
    }

    // Each part is prefixed with its length, so no two lists of parts hash the same bytes.
    private static void Add(IncrementalHash hash, string part)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(part);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

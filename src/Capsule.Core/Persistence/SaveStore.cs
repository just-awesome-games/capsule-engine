using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Capsule.Diagnostics;

namespace Capsule.Persistence;

/// <summary>
/// A run's save documents, held in memory and reached through <c>Run.Saves</c>. Reads are
/// synchronous; a write or delete is persisted by the host after the step that made it, the step
/// that requests exit included, and at the run's teardown. Engine-owned: a run constructs it, and a
/// run with no storage behind it persists and stamps nothing.
/// </summary>
public sealed class SaveStore
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true, NewLine = "\n" };

    private readonly Dictionary<string, Entry> _documents = new(StringComparer.Ordinal);
    private readonly List<string> _names = [];
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);

    // Created on the first write, so a run that never saves allocates no serializer state.
    private ArrayBufferWriter<byte>? _buffer;
    private Utf8JsonWriter? _writer;

    internal SaveStore()
    {
    }

    /// <summary>
    /// Every document present, sorted ordinally; kept sorted on write and delete, never rebuilt.
    /// </summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>Reads the document, deserialized afresh, or the key's fallback when absent.</summary>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="KeyNotFoundException">The document is absent and the key declares no fallback.</exception>
    /// <exception cref="JsonException">The document no longer deserializes as <typeparamref name="T"/>, or is JSON <c>null</c> read as a non-nullable value type.</exception>
    public T Read<T>(SaveKey<T> key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_documents.TryGetValue(key.Name, out Entry entry))
        {
            return JsonSerializer.Deserialize(entry.Document, key.TypeInfo)!;
        }

        return JsonSerializer.Deserialize(
            key.FallbackJson ?? throw new KeyNotFoundException($"No save document is named '{key.Name}' and the key declares no fallback."),
            key.TypeInfo)!;
    }

    /// <summary>
    /// Reads the document when present, and reports absence rather than answering the fallback.
    /// </summary>
    /// <param name="key">The document to read.</param>
    /// <param name="value">The document, deserialized afresh; default when absent.</param>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="JsonException">The document no longer deserializes as <typeparamref name="T"/>.</exception>
    public bool TryRead<T>(SaveKey<T> key, out T value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_documents.TryGetValue(key.Name, out Entry entry))
        {
            value = JsonSerializer.Deserialize(entry.Document, key.TypeInfo)!;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Replaces the document, serialized before this returns so a later mutation of
    /// <paramref name="value"/> never reaches it. Accepted after <c>Run.RequestExit</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="JsonException">The value cannot be serialized; the store is unchanged.</exception>
    public void Write<T>(SaveKey<T> key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);

        string document = Serialize(key, value);

        ref Entry entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_documents, key.Name, out bool present);
        entry.Document = document;

        if (!present)
        {
            _names.Insert(~_names.BinarySearch(key.Name, StringComparer.Ordinal), key.Name);
        }

        _dirty.Add(key.Name);
    }

    /// <summary>Whether the document is present.</summary>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public bool Exists(SaveKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _documents.ContainsKey(key.Name);
    }

    /// <summary>Removes the document. Accepted after <c>Run.RequestExit</c>.</summary>
    /// <returns>Whether it was present.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public bool Delete(SaveKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_documents.Remove(key.Name))
        {
            return false;
        }

        _names.RemoveAt(_names.BinarySearch(key.Name, StringComparer.Ordinal));
        _dirty.Add(key.Name);
        return true;
    }

    /// <summary>
    /// When the host first and last persisted the document; null while absent or not yet
    /// persisted. Host state, restored at boot and set at each flush: a document written this step
    /// shows its stamp from the next, and a game reading it into gameplay has taken an input the
    /// determinism contract does not cover.
    /// </summary>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public SaveMetadata? Metadata(SaveKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _documents.TryGetValue(key.Name, out Entry entry) ? entry.Metadata : null;
    }

    // The host seam. What the storage cannot restore at all propagates: a run booted over saves
    // it cannot read would otherwise persist fresh documents over the player's.
    internal void Restore(ISaveStorage storage) => storage.Restore(Restore);

    // Persists every document written and removes every one deleted since the last flush, stamping
    // what landed with `now`, the host's clock. A storage failure is one warning and the name is
    // dropped until written again; nothing is thrown into the step loop.
    internal void Flush(ISaveStorage storage, DateTimeOffset now)
    {
        if (_dirty.Count == 0)
        {
            return;
        }

        foreach (string name in _dirty)
        {
            ref Entry entry = ref CollectionsMarshal.GetValueRefOrNullRef(_documents, name);

            try
            {
                if (Unsafe.IsNullRef(ref entry))
                {
                    storage.Delete(name);
                    continue;
                }

                SaveMetadata metadata = new(entry.Metadata?.CreatedAt ?? now, now);
                storage.Persist(name, entry.Document, metadata);
                entry.Metadata = metadata;
            }
            catch (Exception failure)
            {
                Log.Warning($"save '{name}' could not be persisted — {failure.Message}");
            }
        }

        _dirty.Clear();
    }

    private void Restore(string name, string document, SaveMetadata metadata)
    {
        ref Entry entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_documents, name, out bool present);
        entry.Document = document;
        entry.Metadata = metadata;

        if (!present)
        {
            _names.Insert(~_names.BinarySearch(name, StringComparer.Ordinal), name);
        }
    }

    private string Serialize<T>(SaveKey<T> key, T value)
    {
        _buffer ??= new ArrayBufferWriter<byte>();
        _writer ??= new Utf8JsonWriter(_buffer, WriterOptions);

        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);

        // The store's writer, never the type info's options: a game context declaring the
        // platform's newline would write CRLF on Windows.
        JsonSerializer.Serialize(_writer, value, key.TypeInfo);
        _writer.Flush();

        return Encoding.UTF8.GetString(_buffer.WrittenSpan);
    }

    private struct Entry
    {
        public string Document;
        public SaveMetadata? Metadata;
    }
}

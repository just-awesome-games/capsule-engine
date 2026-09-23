using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Capsule.Diagnostics;

namespace Capsule.Persistence;

/// <summary>
/// A run's save documents, held in memory and reached through <c>Run.Saves</c>. Reads are
/// synchronous, and a write or delete is persisted by the host after the step that made it and at
/// the run's teardown.
/// </summary>
/// <remarks>A run with no storage behind it persists and stamps nothing.</remarks>
public sealed class SaveStore
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true, NewLine = "\n" };

    private readonly Dictionary<string, Entry> _documents = new(StringComparer.Ordinal);
    private readonly List<string> _names = [];
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);

    // Created on the first write. A run that never saves holds no serializer state.
    private ArrayBufferWriter<byte>? _buffer;
    private Utf8JsonWriter? _writer;

    internal SaveStore()
    {
    }

    /// <summary>
    /// Every document present, sorted ordinally. Invalidated by the next write or delete, each of
    /// which maintains the order in place.
    /// </summary>
    public ReadOnlySpan<string> Names => CollectionsMarshal.AsSpan(_names);

    /// <summary>Reads the document, deserialized afresh, or the key's fallback when absent.</summary>
    /// <exception cref="KeyNotFoundException">The document is absent and the key declares no fallback.</exception>
    /// <exception cref="JsonException">The document no longer deserializes as <typeparamref name="T"/>.</exception>
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

    /// <summary>Reads the document when present, reporting absence instead of using the key's fallback.</summary>
    /// <param name="key">The document to read.</param>
    /// <param name="value">The document, deserialized afresh, or default when absent.</param>
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

    /// <summary>Replaces the document. The value is serialized before this returns.</summary>
    /// <remarks>
    /// A later mutation of <paramref name="value"/> does not reach the store. Accepted after
    /// <c>Run.RequestExit</c>.
    /// </remarks>
    /// <exception cref="JsonException">The value cannot be serialized. The store is unchanged.</exception>
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
    public bool Exists(SaveKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _documents.ContainsKey(key.Name);
    }

    /// <summary>Removes the document. Accepted after <c>Run.RequestExit</c>.</summary>
    /// <returns>Whether it was present.</returns>
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
    /// When the host first and last persisted the document. Null while the document is absent or
    /// not yet persisted.
    /// </summary>
    /// <remarks>
    /// This is host state, restored at boot and set at each flush. A document written this step
    /// shows its stamp from the next step. Reading it into gameplay takes an input the determinism
    /// contract does not cover.
    /// </remarks>
    public SaveMetadata? Metadata(SaveKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _documents.TryGetValue(key.Name, out Entry entry) ? entry.Metadata : null;
    }

    // The host seam. A storage failure to restore propagates, because a run booted over saves it cannot
    // read would otherwise persist fresh documents over the player's.
    internal void Restore(ISaveStorage storage) => storage.Restore(Restore);

    // Persists every document written and removes every one deleted since the last flush, stamping what
    // landed with `now` from the host's clock. A storage failure logs one warning and drops the name
    // until it is written again. Nothing is thrown into the step loop.
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
                Log.Warning($"save '{name}' could not be persisted: {failure.Message}");
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

        // Serialized through the store's writer, not the type info's options. A game context declaring
        // the platform's newline would write CRLF on Windows.
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

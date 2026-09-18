using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Diagnostics;
using Capsule.Persistence;

namespace Capsule.Runtime.Persistence;

/// <summary>
/// The desktop medium: <c>&lt;name&gt;.save.json</c> per document under one directory, created on
/// the first persist. Each write is staged as <c>.save.json.tmp</c> and swapped in, keeping the
/// previous file as <c>.save.json.bak</c>; a file that does not parse at restore is set aside as
/// <c>.save.json.corrupt</c> and its backup restored in its place, with a warning either way. A file the process cannot read
/// at all propagates, and the run does not boot. <c>docs/persistence.md</c> holds the file format.
/// </summary>
public sealed class DirectorySaveStorage : ISaveStorage
{
    private const string Extension = ".save.json";
    private const string StagingSuffix = ".tmp";
    private const string BackupSuffix = ".bak";
    private const string CorruptSuffix = ".corrupt";

    // One level of nesting inside the envelope, the depth every line of the document sits at.
    private const string DocumentIndent = "\n  ";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true, NewLine = "\n" };

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    // Created on the first persist, so a run that never saves allocates no writer.
    private ArrayBufferWriter<byte>? _buffer;
    private Utf8JsonWriter? _writer;

    /// <summary>Stores documents under <paramref name="directory"/>; a relative path resolves against the working directory.</summary>
    /// <exception cref="ArgumentException">The path is null or blank.</exception>
    public DirectorySaveStorage(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory = Path.GetFullPath(directory);
    }

    /// <summary>The directory documents are stored in, as a full path.</summary>
    public string Directory { get; }

    // The default medium: the saves subfolder of the game's per-user local folder.
    internal static DirectorySaveStorage InLocalFolder(string folderName) =>
        new(Path.Combine(LocalFolder.Resolve(folderName), LocalFolder.SavesSubfolder));

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">The callback is null.</exception>
    /// <exception cref="IOException">A file could not be read, moved or copied.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory or a file in it denies access.</exception>
    public void Restore(Action<string, string, SaveMetadata> restore)
    {
        ArgumentNullException.ThrowIfNull(restore);

        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        // Listed before anything is moved: a set-aside changes the directory under a lazy walk.
        foreach (string primary in System.IO.Directory.GetFiles(Directory))
        {
            string file = Path.GetFileName(primary);

            // A name no key can spell was never written by a game, so it is no document either.
            if (!file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                || !SafeName.IsOneSafeDirectoryName(file[..^Extension.Length]))
            {
                continue;
            }

            string name = file[..^Extension.Length];

            if (TryParse(primary, out string document, out SaveMetadata info))
            {
                restore(name, document, info);
                continue;
            }

            string backup = primary + BackupSuffix;
            File.Move(primary, primary + CorruptSuffix, overwrite: true);

            if (File.Exists(backup) && TryParse(backup, out document, out info))
            {
                File.Copy(backup, primary, overwrite: true);
                Log.Warning($"save '{name}' was corrupt and is set aside as '{file}{CorruptSuffix}'; its backup is restored in its place");
                restore(name, document, info);
            }
            else
            {
                Log.Warning($"save '{name}' was corrupt and is set aside as '{file}{CorruptSuffix}'; it has no usable backup, so the document is absent");
            }
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The name is not one safe file name.</exception>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <exception cref="IOException">The staged file could not be written or swapped in.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory or the file denies access.</exception>
    public void Persist(string name, string document, SaveMetadata metadata)
    {
        ThrowIfUnsafe(name);
        ArgumentNullException.ThrowIfNull(document);

        _buffer ??= new ArrayBufferWriter<byte>();
        _writer ??= new Utf8JsonWriter(_buffer, WriterOptions);

        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);

        _writer.WriteStartObject();
        _writer.WritePropertyName("metadata");
        JsonSerializer.Serialize(_writer, metadata, SaveFileJsonContext.Default.SaveMetadata);
        _writer.WritePropertyName("document");

        // A JSON string never holds a raw newline, so every newline in the document is between
        // tokens and pushing each line in by the envelope's depth changes no value; the read strips
        // exactly this and hands the text back as written.
        _writer.WriteRawValue(document.Replace("\n", DocumentIndent, StringComparison.Ordinal));
        _writer.WriteEndObject();
        _writer.Flush();
        _buffer.Write("\n"u8);

        System.IO.Directory.CreateDirectory(Directory);

        string primary = PathOf(name);
        string staging = primary + StagingSuffix;

        File.WriteAllBytes(staging, _buffer.WrittenSpan);

        if (File.Exists(primary))
        {
            // Metadata errors ignored: on exFAT, FAT and some network volumes — where a portable
            // build beside its executable lives — ReplaceFile cannot merge ACLs or streams and
            // would fail the whole swap over metadata the save never needed.
            File.Replace(staging, primary, primary + BackupSuffix, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(staging, primary);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Removes the file, its backup and any staged write; a set-aside corrupt file stays.</remarks>
    /// <exception cref="ArgumentException">The name is not one safe file name.</exception>
    /// <exception cref="IOException">A file is in use.</exception>
    /// <exception cref="UnauthorizedAccessException">A file denies access.</exception>
    public void Delete(string name)
    {
        ThrowIfUnsafe(name);

        string primary = PathOf(name);

        File.Delete(primary);
        File.Delete(primary + BackupSuffix);
        File.Delete(primary + StagingSuffix);
    }

    private static bool TryParse(string path, out string document, out SaveMetadata metadata)
    {
        byte[] bytes = File.ReadAllBytes(path);

        // A hand-edited file may carry a byte-order mark, which the parser does not skip.
        ReadOnlyMemory<byte> text = bytes.AsSpan().StartsWith(Utf8Bom) ? bytes.AsMemory(Utf8Bom.Length) : bytes;

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(text);
            JsonElement root = parsed.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("metadata", out JsonElement head)
                && head.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("document", out JsonElement body))
            {
                metadata = head.Deserialize(SaveFileJsonContext.Default.SaveMetadata);
                document = body.GetRawText().Replace(DocumentIndent, "\n", StringComparison.Ordinal);

                return true;
            }
        }
        catch (JsonException)
        {
        }

        document = "";
        metadata = default;

        return false;
    }

    private static void ThrowIfUnsafe(string name)
    {
        if (!SafeName.IsOneSafeDirectoryName(name))
        {
            throw new ArgumentException("A save name must be one safe file name.", nameof(name));
        }
    }

    private string PathOf(string name) => Path.Combine(Directory, name + Extension);
}

// The envelope's `metadata` half: camel-cased ISO-8601 instants with their offsets. Reflection-based
// serialization is off solution-wide, so this generated context is the only way it is written or read.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SaveMetadata))]
internal sealed partial class SaveFileJsonContext : JsonSerializerContext;

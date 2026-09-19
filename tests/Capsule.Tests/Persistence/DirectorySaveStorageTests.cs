using System.Text;
using Capsule.Diagnostics;
using Capsule.Persistence;
using Capsule.Runtime.Persistence;
using Capsule.Tests.Runtime;

namespace Capsule.Tests.Persistence;

[Collection(LogSinkCollection.Name)]
public sealed class DirectorySaveStorageTests : IDisposable
{
    private const string Document = "{\n  \"Volume\": 5,\n  \"Name\": \"a\"\n}";

    private static readonly SaveMetadata Metadata = new(
        new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.FromHours(1)),
        new DateTimeOffset(2026, 9, 18, 11, 30, 0, TimeSpan.FromHours(-7)));

    private readonly TempWorkspace _workspace = new(nameof(DirectorySaveStorageTests));
    private readonly CollectingLogSink _log = new();

    public DirectorySaveStorageTests() => Log.UseSink(_log);

    public void Dispose()
    {
        Log.UseSink(null);
        _workspace.Dispose();
    }

    private string Saves => Path.Combine(_workspace.Root, "saves");

    private string Primary => Path.Combine(Saves, "settings.save.json");

    // The file is one a person reads and hand-edits — no byte-order mark, LF alone, the envelope's
    // two halves — and what comes back is the document text and metadata exactly as persisted.
    [Fact]
    public void PersistThenRestore_RoundTripsTheDocumentAndTheMetadata_ThroughAnExactFile()
    {
        new DirectorySaveStorage(Saves).Persist("settings", Document, Metadata);

        Assert.Equal(
            """
            {
              "metadata": {
                "createdAt": "2026-09-17T10:00:00+01:00",
                "updatedAt": "2026-09-18T11:30:00-07:00"
              },
              "document": {
                "Volume": 5,
                "Name": "a"
              }
            }

            """.ReplaceLineEndings("\n"),
            Encoding.UTF8.GetString(File.ReadAllBytes(Primary)));
        Assert.False(File.ReadAllBytes(Primary).AsSpan().StartsWith(Encoding.UTF8.Preamble));

        (string document, SaveMetadata metadata) = Assert.Single(Restore()).Value;
        Assert.Equal(Document, document);
        Assert.Equal(Metadata, metadata);
        Assert.Empty(_log.Entries);
    }

    // A second persist keeps the previous file as the backup; a corrupt primary is set aside over
    // an earlier set-aside and the backup restored in its place with a warning; nothing but a
    // `.json` is ever a document.
    [Fact]
    public void ACorruptPrimary_IsSetAsideAndTheBackupRestoredInItsPlace()
    {
        DirectorySaveStorage storage = new(Saves);
        storage.Persist("settings", "1", Metadata);
        storage.Persist("settings", "2", Metadata);
        File.WriteAllText(Primary + ".corrupt", "earlier");
        File.WriteAllText(Primary + ".tmp", "staged");
        File.Copy(Primary, Path.Combine(Saves, "notes.txt"));
        File.Copy(Primary, Path.Combine(Saves, "plain.json"));
        File.WriteAllText(Primary, "{ not json");

        Assert.Equal("1", Assert.Single(Restore()).Value.Document);

        Assert.Equal("{ not json", File.ReadAllText(Primary + ".corrupt"));
        Assert.Contains("\"document\": 1", File.ReadAllText(Primary), StringComparison.Ordinal);
        LogEntry warning = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("backup", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"document\": 1 }")]
    [InlineData("{ \"metadata\": 3, \"document\": 1 }")]
    public void ACorruptPrimaryWithNoBackup_IsAbsentWithAWarning(string contents)
    {
        Directory.CreateDirectory(Saves);
        File.WriteAllText(Primary, contents);

        Assert.Empty(Restore());
        Assert.False(File.Exists(Primary));
        Assert.Equal(contents, File.ReadAllText(Primary + ".corrupt"));
        Assert.Contains("absent", Assert.Single(_log.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_RemovesThePrimaryBackupAndStagedWrite_AndLeavesTheSetAside()
    {
        DirectorySaveStorage storage = new(Saves);
        storage.Persist("settings", "1", Metadata);
        storage.Persist("settings", "2", Metadata);
        File.WriteAllText(Primary + ".corrupt", "old");
        File.WriteAllText(Primary + ".tmp", "staged");

        storage.Delete("settings");

        Assert.Equal([Primary + ".corrupt"], Directory.GetFiles(Saves));
    }

    // The file invites hand-editing, and an editor may write a byte-order mark: that is a document,
    // not corruption.
    [Fact]
    public void APrimaryWrittenWithAByteOrderMark_RestoresAsTheDocument()
    {
        new DirectorySaveStorage(Saves).Persist("settings", Document, Metadata);
        File.WriteAllBytes(Primary, [.. Encoding.UTF8.Preamble, .. File.ReadAllBytes(Primary)]);

        Assert.Equal(Document, Assert.Single(Restore()).Value.Document);
        Assert.Empty(_log.Entries);
    }

    // Pins schema tolerance for a hand-edited file: a field the game does not declare is ignored and
    // one it declares but the file omits takes its default, so the fields it knows come back intact.
    [Fact]
    public void AHandWrittenDocument_RestoresItsKnownFieldsPastAnUnknownAndAMissingOne()
    {
        Directory.CreateDirectory(Saves);
        File.WriteAllText(
            Primary,
            """
            {
              "metadata": {
                "createdAt": "2026-09-17T10:00:00+01:00",
                "updatedAt": "2026-09-18T11:30:00-07:00"
              },
              "document": {
                "Volume": 5,
                "Trophies": 3
              }
            }
            """);

        SaveStore saves = new();
        saves.Restore(new DirectorySaveStorage(Saves));
        Settings restored = saves.Read(new SaveKey<Settings>("settings", SaveTestJsonContext.Default.Settings));

        Assert.Equal(5, restored.Volume);
        Assert.Equal(string.Empty, restored.Name);
        Assert.Empty(_log.Entries);
    }

    private Dictionary<string, (string Document, SaveMetadata Metadata)> Restore()
    {
        Dictionary<string, (string, SaveMetadata)> restored = new(StringComparer.Ordinal);
        new DirectorySaveStorage(Saves).Restore((name, document, metadata) => restored[name] = (document, metadata));

        return restored;
    }
}

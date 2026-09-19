using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Persistence;
using Capsule.Tests.Runtime;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Persistence;

[Collection(LogSinkCollection.Name)]
public sealed class SavePersistenceTests : IDisposable
{
    private static readonly SaveKey<int> Visits = new("visits", SaveTestJsonContext.Default.Int32, 0);

    private readonly TempWorkspace _workspace = new(nameof(SavePersistenceTests));
    private readonly CollectingLogSink _log = new();

    public SavePersistenceTests() => Log.UseSink(_log);

    public void Dispose()
    {
        Log.UseSink(null);
        _workspace.Dispose();
    }

    // The whole desktop path end to end: a headless run given a directory writes the file — the
    // last write in the step that ends the run included — and the next run reads it before its
    // first scene starts. The file is LF whatever the game's context declares.
    [Fact]
    public void AHeadlessRunWithASaveDirectory_WritesTheFileASecondRunReads()
    {
        string saves = Path.Combine(_workspace.Root, "saves");
        List<int> seen = [];

        for (int run = 0; run < 2; run++)
        {
            HeadlessRunResult result = CapsuleEngine.Configure(
                    "Save Game",
                    new DesktopPlatform(),
                    new SceneRegistry(
                        new EntityRegistry([]),
                        [SceneRegistration.Plain(typeof(Counting), _ => new Counting(seen.Add))]))
                .WithFixedStep(10)
                .WithSaveDirectory(saves)
                .WithoutCrashLog()
                .WithLogSink(_log)
                .RunHeadless<Counting>(new InputScript().Wait(1).Build());

            Assert.Equal(1, result.Steps);
        }

        // Each run reads at start, then writes at start and once per step: two ahead by its end.
        Assert.Equal([0, 2], seen);
        Assert.DoesNotContain('\r', File.ReadAllText(Path.Combine(saves, "visits.save.json")));
        Assert.Empty(_log.Entries);
    }

    // The stamp is host state: unstamped through the step that wrote it, stamped once the flush
    // after that step persisted it, with the creation time carried forward by later flushes.
    [Fact]
    public void MetadataWrittenThisStep_IsVisibleNextStepAndCarriesItsCreationForward()
    {
        MemorySaveStorage storage = new();
        List<SaveMetadata?> metadata = [];

        using SceneHost host = new(ToScene<Counting>(), (in SceneTransition _) => new Counting(null, metadata.Add), new Run(), storage);

        host.Step(SceneFixtures.Step(0));
        host.FlushSaves();
        SaveMetadata? first = host.Run.Saves.Metadata(Visits);

        host.Step(SceneFixtures.Step(1));
        host.FlushSaves();
        SaveMetadata? second = host.Run.Saves.Metadata(Visits);

        Assert.Equal([null, first], metadata);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Value.CreatedAt, second.Value.CreatedAt);
        Assert.True(second.Value.UpdatedAt >= first.Value.UpdatedAt);
        Assert.Equal(second, storage.Metadata["visits"]);
    }

    // A medium that fails is a warning, never an exception into the step loop; the write is dropped
    // and the next write of the name is the retry.
    [Fact]
    public void AStorageThatThrows_LogsAWarningAndTheRunContinues()
    {
        ThrowingSaveStorage storage = new();
        using SceneHost host = new(ToScene<Idle>(), (in SceneTransition _) => new Idle(), new Run(), storage);

        host.Run.Saves.Write(Visits, 1);
        host.FlushSaves();
        host.Step(SceneFixtures.Step(0));
        host.FlushSaves();

        LogEntry warning = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("visits", warning.Message, StringComparison.Ordinal);
        Assert.Contains("disk full", warning.Message, StringComparison.Ordinal);
        Assert.Null(host.Run.Saves.Metadata(Visits));
        Assert.Equal(1, storage.Attempts);
    }

    // A window closed from outside the run tears the host down with no step after it: what the
    // scene's stop wrote lands through the flush at disposal.
    [Fact]
    public void ADocumentWrittenInOnStop_IsPersistedByDispose()
    {
        MemorySaveStorage storage = new();
        SceneHost host = new(ToScene<SavingOnStop>(), (in SceneTransition _) => new SavingOnStop(), new Run(), storage);

        host.Step(SceneFixtures.Step(0));
        host.FlushSaves();
        Assert.Empty(storage.Documents);

        host.Dispose();

        Assert.Equal("7", storage.Documents["visits"]);
    }

    private static SceneTransition ToScene<TScene>()
        where TScene : Scene
        => SceneTransition.ToScene(typeof(TScene), null);

    private sealed class Idle : Scene;

    // Reads the counter at start and writes one more at start and each step, reporting what it
    // read on the way in and the stamp it sees while writing.
    private sealed class Counting(Action<int>? read, Action<SaveMetadata?>? metadataSeen = null) : Scene
    {
        protected override void OnStart()
        {
            int visits = Run.Saves.Read(Visits);
            read?.Invoke(visits);
            Run.Saves.Write(Visits, visits + 1);
        }

        protected override void OnStep(in StepContext context)
        {
            Run.Saves.Write(Visits, Run.Saves.Read(Visits) + 1);
            metadataSeen?.Invoke(Run.Saves.Metadata(Visits));
        }
    }

    private sealed class SavingOnStop : Scene
    {
        protected override void OnStop() => Run.Saves.Write(Visits, 7);
    }

    private sealed class MemorySaveStorage : ISaveStorage
    {
        internal Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, SaveMetadata> Metadata { get; } = new(StringComparer.Ordinal);

        public void Restore(Action<string, string, SaveMetadata> restore)
        {
            foreach ((string name, string document) in Documents)
            {
                restore(name, document, Metadata[name]);
            }
        }

        public void Persist(string name, string document, SaveMetadata metadata)
        {
            Documents[name] = document;
            Metadata[name] = metadata;
        }

        public void Delete(string name)
        {
            Documents.Remove(name);
            Metadata.Remove(name);
        }
    }

    private sealed class ThrowingSaveStorage : ISaveStorage
    {
        internal int Attempts { get; private set; }

        public void Restore(Action<string, string, SaveMetadata> restore)
        {
        }

        public void Persist(string name, string document, SaveMetadata metadata)
        {
            Attempts++;

            throw new IOException("disk full");
        }

        public void Delete(string name)
        {
        }
    }
}

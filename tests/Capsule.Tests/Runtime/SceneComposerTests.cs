using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Capsule.Runtime;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Allocation;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Runtime;

// Reaches for the shell content directory a scene document ships into, which is one place for the
// whole process. The collection keeps it away from everything else that writes there.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class SceneComposerTests : IDisposable
{
    private const string DocumentName = "hall";

    private static readonly string DocumentPath =
        Path.Combine(AppContext.BaseDirectory, "assets", DocumentName + ShippedSceneDocument.Extension);

    // A game boots a document-backed scene by its class. Nothing in game code names the document.
    // Losing the turn from one into the other boots an empty room rather than failing.
    [Fact]
    public void ADocumentBackedClass_BootedByItsClass_IsComposedFromTheDocumentItClaims()
    {
        Write(SceneFixtures.Room(new SceneDocumentEntry("chest", new EntitySpawn(new Vector2(48f, 16f)))));
        SceneComposer composer = new(Registry(), new HostPlatform());

        Scene composed = composer.Resolve(SceneTransition.ToScene(typeof(Hall), null));

        Assert.IsType<Hall>(composed);
        Assert.IsType<SceneFixtures.Placed>(Assert.Single(composed.Entities.ToArray()));
    }

    // The scene layer is pure and knows no paths. Without this the commonest authoring mistake
    // leaves the author guessing which document holds the type key it names.
    [Fact]
    public void APlacementNoEntityClaims_NamesTheDocumentFileThatHoldsIt()
    {
        Write(SceneFixtures.Room(new SceneDocumentEntry("wyvern")));
        SceneComposer composer = new(Registry(), new HostPlatform());

        SpawnException failure = Assert.Throws<SpawnException>(
            () => composer.Resolve(SceneTransition.ToName(DocumentName, null)));

        Assert.Contains(DocumentName + ShippedSceneDocument.Extension, failure.Message, StringComparison.Ordinal);
        Assert.Contains("wyvern", failure.Message, StringComparison.Ordinal);
    }

    // A shipped file that is not gzip reports the documented exception type with its path.
    [Fact]
    public void AShippedDocumentThatIsNotGzip_FailsAsAMalformedDocumentNamingItsFile()
    {
        Write(SceneFixtures.Room(), gzipped: false);
        SceneComposer composer = new(Registry(), new HostPlatform());

        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(
            () => composer.Resolve(SceneTransition.ToName(DocumentName, null)));

        Assert.Contains(DocumentName + ShippedSceneDocument.Extension, failure.Message, StringComparison.Ordinal);
        Assert.Contains("not valid gzip", failure.Message, StringComparison.Ordinal);
    }

    // The reader sizes its buffer from the gzip trailer. A damaged trailer claiming far more than the file
    // holds, though deflate could reach it, is refused as damage without allocating what it claims.
    [Fact]
    public void AShippedDocumentWhoseTrailerOverstatesItsLength_FailsAsAMalformedDocumentWithoutAllocatingTheClaim()
    {
        // A stage's document, large enough that the claim dwarfs what a failed read costs.
        Write(StageWorkload.Build());
        byte[] shipped = File.ReadAllBytes(DocumentPath);
        uint claimed = (uint)shipped.Length * 1000;
        BinaryPrimitives.WriteUInt32LittleEndian(shipped.AsSpan(shipped.Length - 4), claimed);
        File.WriteAllBytes(DocumentPath, shipped);
        SceneComposer composer = new(Registry(), new HostPlatform());

        long before = StepMeasurement.Start();
        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(
            () => composer.Resolve(SceneTransition.ToName(DocumentName, null)));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains("not valid gzip", failure.Message, StringComparison.Ordinal);
        Assert.InRange(allocated, 0, claimed / 4);
    }

    // A prefetch reads its document on a worker and has 0.1 seconds of simulated time, six steps at 60 a second,
    // before it builds its scene. A request before then builds the scene on its own step, and one after starts the
    // scene built at step 6. Both wait for the read, and a read finishing early moves nothing earlier.
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 300)]
    [InlineData(7, 0)]
    [InlineData(7, 300)]
    public void APrefetchedDocument_OpensOnTheSameStepsHoweverLongItsReadTakes(int requestStep, int readMs)
    {
        Write(SceneFixtures.Room(new SceneDocumentEntry("chest", new EntitySpawn(new Vector2(48f, 16f)))));
        List<string> log = [];
        long tick = 0;
        SceneComposer composer = new(Registry(log, () => tick, requestStep), new SlowPlatform(readMs));
        using SceneHost host = new(SceneTransition.ToScene(typeof(Lobby), null), composer.Resolve, new Run(), composer: composer);

        for (; tick < requestStep + 2; tick++)
        {
            host.Step(SceneFixtures.Step(tick));

            // A read that finished before the step that builds its scene changes nothing.
            if (readMs == 0)
            {
                Thread.Sleep(50);
            }
        }

        string[] expected = requestStep == 1
            ? ["lobby step 0", "lobby step 1", "hall built at 1", "hall step 2"]
            : [.. Enumerable.Range(0, 7).Select(step => "lobby step " + step), "hall built at 6", "lobby step 7", "hall step 8"];
        Assert.Equal(expected, log);
        Assert.IsType<Hall>(host.Scene);
    }

    // The worker's failure reaches the step that waits for its read as the type a read on the game thread
    // raises, naming the file, and the steps before it raise nothing.
    [Fact]
    public void APrefetchedDocumentThatIsNotGzip_FailsTheStepThatBuildsItsScene()
    {
        Write(SceneFixtures.Room(), gzipped: false);
        SceneComposer composer = new(Registry(), new SlowPlatform(50));
        using SceneHost host = new(SceneTransition.ToScene(typeof(Lobby), null), composer.Resolve, new Run(), composer: composer);

        for (long tick = 0; tick < 6; tick++)
        {
            host.Step(SceneFixtures.Step(tick));
        }

        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(
            () => host.Step(SceneFixtures.Step(6)));

        Assert.Contains(DocumentName + ShippedSceneDocument.Extension, failure.Message, StringComparison.Ordinal);
        Assert.Contains("not valid gzip", failure.Message, StringComparison.Ordinal);
    }

    // A restart composes the running scene's document again without opening it, after a prefetch read and composed
    // another one, and lets go of the other, which a later prefetch reads again.
    [Fact]
    public void ARestartAfterAPrefetchOfAnotherDocument_ReadsNothingAndKeepsOneDocument()
    {
        Write(SceneFixtures.Room());
        string annexPath = Path.Combine(Path.GetDirectoryName(DocumentPath)!, "annex" + ShippedSceneDocument.Extension);
        ShippedSceneDocument.Write(SceneFixtures.Room(), annexPath, CompressionLevel.Fastest);
        try
        {
            GatedPlatform platform = new();
            SceneComposer composer = new(
                new SceneRegistry(SceneFixtures.Registry(), [SceneRegistration.FromDocument(typeof(Ward), DocumentName, content => new Ward(content!.Value))]),
                platform);
            using SceneHost host = new(SceneTransition.ToScene(typeof(Ward), null), composer.Resolve, new Run(), composer: composer);

            Scene first = host.Scene;
            for (long tick = 0; tick < 10; tick++)
            {
                host.Step(SceneFixtures.Step(tick));
            }

            Assert.Equal(2, platform.Opens);
            Assert.NotSame(first, Assert.IsType<Ward>(host.Scene));
            platform.Gated = true;
            Assert.False(composer.BeginRead(SceneTransition.ToScene(typeof(Ward), null)));
            Assert.True(composer.BeginRead(SceneTransition.ToName("annex", null)));
            composer.EndRead();
            platform.Gate.Set();
        }
        finally
        {
            File.Delete(annexPath);
        }
    }

    // A later prefetch of another document ends the earlier read. Nothing keeps a superseded read or its document.
    [Fact]
    public void ConsecutivePrefetchesOfDifferentDocuments_HoldOneRead()
    {
        SceneComposer composer = new(Registry(), new GatedPlatform());

        composer.BeginRead(SceneTransition.ToName(DocumentName, null));
        composer.BeginRead(SceneTransition.ToName("annex", null));

        Assert.Equal("annex", composer.Reading);
    }

    // A host torn down with a read still waiting for a turn or still reading owns nothing after, and a read that
    // had not taken its turn never opens its file, even when a turn frees the moment the host is torn down. One
    // already reading runs to its end.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisposingTheHostMidRead_LeavesNoReadOwnedAndStartsNoQueuedRead(bool queued)
    {
        Write(SceneFixtures.Room());
        GatedPlatform platform = new();
        SceneComposer composer = new(Registry(), platform);
        SceneHost host = new(SceneTransition.ToScene(typeof(Lobby), null), composer.Resolve, new Run(), composer: composer);
        using CountdownEvent turnsTaken = new(AssetDecodes.Concurrency);
        using ManualResetEventSlim firstBack = new();
        using ManualResetEventSlim turnsBack = new();

        // The first holder tears the host down and frees its turn straight after, on one thread.
        Task<int>[] holders = queued
            ? [.. Enumerable.Range(0, AssetDecodes.Concurrency).Select(i => AssetDecodes.Run(
                () => Hold(turnsTaken, i == 0 ? firstBack : turnsBack, i == 0 ? host.Dispose : null),
                CancellationToken.None))]
            : [];
        if (queued)
        {
            Assert.True(turnsTaken.Wait(TimeSpan.FromSeconds(10)), "the decode turns were never all taken");
        }
        else
        {
            platform.Gated = true;
        }

        host.Step(SceneFixtures.Step(0));
        if (!queued)
        {
            Assert.True(platform.Entered.Wait(TimeSpan.FromSeconds(10)), "the read never opened its file");
        }

        int opened = platform.Opens;
        if (queued)
        {
            firstBack.Set();
            await holders[0];
        }
        else
        {
            host.Dispose();
        }

        turnsBack.Set();
        platform.Gate.Set();
        await Task.WhenAll(holders);

        Assert.Null(composer.Reading);
        Assert.False(SpinWait.SpinUntil(() => platform.Opens > opened, 200), "a read started after the host was torn down");
    }

    public void Dispose() => File.Delete(DocumentPath);

    private static int Hold(CountdownEvent taken, ManualResetEventSlim back, Action? beforeFreeing)
    {
        taken.Signal();
        back.Wait();
        beforeFreeing?.Invoke();

        return 0;
    }

    // Ships document at DocumentPath, or writes its plain JSON there when it is not to be gzipped.
    private static void Write(SceneDocument document, bool gzipped = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DocumentPath)!);
        if (!gzipped)
        {
            File.WriteAllText(DocumentPath, document.ToJson());
            return;
        }

        ShippedSceneDocument.Write(document, DocumentPath, CompressionLevel.Fastest);
    }

    private static SceneRegistry Registry() => Registry([], static () => 0, requestStep: -1);

    // The hall logs each build at the tick it happened on, and the lobby prefetches it at step 0 and requests it
    // at requestStep.
    private static SceneRegistry Registry(List<string> log, Func<long> tick, int requestStep) =>
        new(
            SceneFixtures.Registry(("chest", static spawn => new SceneFixtures.Placed(spawn))),
            [
                SceneRegistration.FromDocument(typeof(Hall), DocumentName, content => new Hall(content!.Value, log, tick)),
                SceneRegistration.Plain(typeof(Lobby), _ => new Lobby(log, requestStep)),
            ]);

    private sealed class Hall : Scene
    {
        private readonly List<string> _log;

        public Hall(SceneContent content, List<string> log, Func<long> tick)
            : base(content)
        {
            _log = log;
            log.Add("hall built at " + tick());
        }

        protected override void OnStep(in StepContext context) => _log.Add("hall step " + context.Tick);
    }

    // Prefetches the annex document at step 0 and restarts itself at step 8, once the annex is built.
    private sealed class Ward(SceneContent content) : Scene(content)
    {
        protected override void OnStep(in StepContext context)
        {
            if (context.Tick == 0)
            {
                Run.PrefetchScene(new SceneKey("annex"));
            }
            else if (context.Tick == 8)
            {
                Run.RequestRestart();
            }
        }
    }

    private sealed class Lobby(List<string> log, int requestStep) : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            log.Add("lobby step " + context.Tick);
            if (context.Tick == 0)
            {
                Run.PrefetchScene<Hall>();
            }
            else if (context.Tick == requestStep)
            {
                Run.RequestScene<Hall>();
            }
        }
    }

    // Shipped content from the directory the documents ship into, counting each open. A gated platform holds an
    // open until Gate is set, then fails it.
    private sealed class GatedPlatform : HostPlatform
    {
        private int _opens;

        internal bool Gated { get; set; }

        internal ManualResetEventSlim Entered { get; } = new();

        internal ManualResetEventSlim Gate { get; } = new();

        internal int Opens => Volatile.Read(ref _opens);

        protected internal override Stream OpenContent(string relativePath)
        {
            Interlocked.Increment(ref _opens);
            Entered.Set();
            if (Gated)
            {
                // A gated read ends without the file, which a spec's cleanup may then delete.
                Gate.Wait();
                throw new IOException("The gated read was let go.");
            }

            return base.OpenContent(relativePath);
        }
    }

    // Shipped content that takes readMs to open, from the directory the documents ship into.
    private sealed class SlowPlatform(int readMs) : HostPlatform
    {
        protected internal override Stream OpenContent(string relativePath)
        {
            Thread.Sleep(readMs);

            return base.OpenContent(relativePath);
        }
    }
}

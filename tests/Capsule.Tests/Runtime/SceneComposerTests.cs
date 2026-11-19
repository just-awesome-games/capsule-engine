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
        Directory.CreateDirectory(Path.GetDirectoryName(DocumentPath)!);
        File.WriteAllText(DocumentPath, SceneFixtures.Room().ToJson());
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

        long before = GC.GetAllocatedBytesForCurrentThread();
        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(
            () => composer.Resolve(SceneTransition.ToName(DocumentName, null)));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains("not valid gzip", failure.Message, StringComparison.Ordinal);
        Assert.InRange(allocated, 0, claimed / 4);
    }

    // A prefetch reads its document on a worker. However long the read takes, the prefetch builds its scene at
    // the end of the step after it, and a request opens the scene at the boundary it asked for, both waiting
    // for the read. A read finishing early moves nothing earlier. A request after the build starts the built
    // scene, and one before it builds the scene there.
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 300)]
    [InlineData(2, 0)]
    [InlineData(2, 300)]
    public void APrefetchedDocument_OpensOnTheSameStepsHoweverLongItsReadTakes(int requestStep, int readMs)
    {
        Write(SceneFixtures.Room(new SceneDocumentEntry("chest", new EntitySpawn(new Vector2(48f, 16f)))));
        List<string> log = [];
        long tick = 0;
        SceneComposer composer = new(Registry(log, () => tick, requestStep), new SlowPlatform(readMs));
        using SceneHost host = new(SceneTransition.ToScene(typeof(Lobby), null), composer.Resolve, new Run(), composer: composer);

        for (; tick < 4; tick++)
        {
            host.Step(SceneFixtures.Step(tick));

            // A read that finished before the step that builds its scene changes nothing.
            if (readMs == 0)
            {
                Thread.Sleep(50);
            }
        }

        string[] expected = requestStep == 1
            ? ["lobby step 0", "lobby step 1", "hall built at 1", "hall step 2", "hall step 3"]
            : ["lobby step 0", "lobby step 1", "hall built at 1", "lobby step 2", "hall step 3"];
        Assert.Equal(expected, log);
        Assert.IsType<Hall>(host.Scene);
    }

    // The worker's failure reaches the step that waits for its read as the type a read on the game thread
    // raises, naming the file, and the step that started the read raises nothing.
    [Fact]
    public void APrefetchedDocumentThatIsNotGzip_FailsTheStepThatBuildsItsScene()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DocumentPath)!);
        File.WriteAllText(DocumentPath, SceneFixtures.Room().ToJson());
        SceneComposer composer = new(Registry(), new SlowPlatform(50));
        using SceneHost host = new(SceneTransition.ToScene(typeof(Lobby), null), composer.Resolve, new Run(), composer: composer);

        host.Step(SceneFixtures.Step(0));
        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(
            () => host.Step(SceneFixtures.Step(1)));

        Assert.Contains(DocumentName + ShippedSceneDocument.Extension, failure.Message, StringComparison.Ordinal);
        Assert.Contains("not valid gzip", failure.Message, StringComparison.Ordinal);
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
    // had not taken its turn never opens its file. One already reading runs to its end.
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
        using ManualResetEventSlim turnsBack = new();
        Task<int>[] holders = queued
            ? [.. Enumerable.Range(0, AssetDecodes.Concurrency).Select(_ => AssetDecodes.Run(() => Hold(turnsTaken, turnsBack), CancellationToken.None))]
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

        host.Dispose();
        int opened = platform.Opens;
        turnsBack.Set();
        platform.Gate.Set();
        await Task.WhenAll(holders);

        Assert.Null(composer.Reading);
        Assert.False(SpinWait.SpinUntil(() => platform.Opens > opened, 200), "a read started after the host was torn down");
    }

    public void Dispose() => File.Delete(DocumentPath);

    private static int Hold(CountdownEvent taken, ManualResetEventSlim back)
    {
        taken.Signal();
        back.Wait();

        return 0;
    }

    private static void Write(SceneDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DocumentPath)!);
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

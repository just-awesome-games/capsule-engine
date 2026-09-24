using System.Globalization;
using System.Numerics;
using Capsule.Runtime.Desktop;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;
using Xunit.Abstractions;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class StageAllocationTests(ITestOutputHelper output)
{
    private const int WarmupSteps = 240;
    private const int MeasuredSteps = 600;
    private const int SparksSpawned = MeasuredSteps / StageWorkload.StepsBetweenSpawns;

    // One spark is an entity, its component list and a renderer; 512 bytes is well over twice
    // what those cost, and well under what one stray per-step allocation in the engine would add.
    private const long SpawnBytesEach = 512;

    [Fact]
    public void AStageThatSpawnsNothing_AllocatesNothing()
    {
        SceneDocument document = StageWorkload.Build();

        Report("no structural change", Measure(document, StageChurn.None), maxPerRun: 0);

        // The draw list is derived and rebuilt whole whenever anything joins or leaves. Rebuilding
        // it every step must still cost no allocation at all.
        Report("draw list every step", Measure(document, StageChurn.DrawListOnly), maxPerRun: 0);
    }

    [Fact]
    public void AStageSpawningTwentyEntitiesASecond_AllocatesOnlyWhatItSpawns()
    {
        Report(
            "20 spawns and despawns a second",
            Measure(StageWorkload.Build(), StageChurn.Spawning),
            maxPerRun: SparksSpawned * SpawnBytesEach);
    }

    [Fact]
    public void AStageSpawningFromAPool_AllocatesNothing()
    {
        Report(
            "20 spawns and despawns a second, from a pool",
            Measure(StageWorkload.Build(), StageChurn.Pooled),
            maxPerRun: 0);
    }

    // A wave spawned in one step is queued and drained at the end of it. The queue and its
    // membership index are the only things the drain itself keeps, so the step allocates what the
    // entities cost and little more; how long the drain takes is the bench's to measure.
    [Fact]
    public void AWaveOfDeferredAdds_AllocatesNoMoreThanTheEntitiesItSpawns()
    {
        // Warmed first, so the measurement is not taken against a cold JIT.
        Drain(256);

        const int Wave = 65536;
        long bytes = Drain(Wave);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Wave} deferred adds: {bytes} bytes ({(double)bytes / Wave:0} each)"));

        Assert.True(
            bytes < Wave * SpawnBytesEach,
            FormattableString.Invariant($"{Wave} deferred adds allocated {bytes} bytes, more than the entities themselves account for."));

        // This test is the one thing in the collection that leaves a heap behind it. Its neighbours
        // assert that a step allocates nothing at all, and a background collection of these entities
        // landing inside one of their measured windows is enough to make that read as a per-step
        // allocation. Handed back here rather than left for whoever runs next.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static long Drain(int count)
    {
        Scene scene = new();
        using SceneSimulation simulation = new(scene);

        // From Update, not from scene entry: only a mutation requested mid-step is deferred, and
        // the deferred queue is the thing being measured.
        scene.Add(new Spawner(count));

        long bytes = GC.GetAllocatedBytesForCurrentThread();
        simulation.Step(SceneFixtures.Step(0));

        return GC.GetAllocatedBytesForCurrentThread() - bytes;
    }

    private sealed class Spawner(int count) : Entity(Vector2.Zero)
    {
        private bool _spawned;

        protected internal override void OnStep(in StepContext context)
        {
            if (_spawned)
            {
                return;
            }

            _spawned = true;

            for (int index = 0; index < count; index++)
            {
                Scene!.Add(new SceneFixtures.Drifter(Vector2.Zero));
            }
        }
    }

    // The most frequent transition a game performs is dying and resuming at a checkpoint, and it
    // happens between two fixed steps with a frame waiting on it. Deleting the document first is
    // the assertion: a restart that read the file could not survive it.
    [Fact]
    public void ARestart_ComposesTheStageAgainWithoutReadingItsDocumentFromDisk()
    {
        // Beside the executable, where SceneComposer looks; SceneComposerTests writes here too.
        // StageAllocationCollection disables parallelization, so the delete below cannot race it.
        string directory = Path.Combine(AppContext.BaseDirectory, "assets");
        string path = Path.Combine(directory, StageWorkload.DocumentName + ".scene.json");
        Directory.CreateDirectory(directory);

        try
        {
            SceneDocumentFile.Save(StageWorkload.Build(), path);

            SceneComposer composer = new(StageWorkload.Scenes(), new DesktopPlatform());
            using SceneHost host = new(
                SceneTransition.ToName(StageWorkload.DocumentName, null),
                composer.Resolve,
                StageWorkload.Defaults);

            Scene opened = host.Scene;
            File.Delete(path);

            for (int index = 0; index < 15; index++)
            {
                host.Run.RequestRestart();
                host.Step(Scenes.SceneFixtures.Step(index));
            }

            Assert.IsType<StageWorkload.StageScene>(host.Scene);
            Assert.NotSame(opened, host.Scene);
            Assert.Equal(StageWorkload.PlacedEntities + 1, host.Scene.Entities.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (StepSample[] Samples, int Entities) Measure(SceneDocument document, StageChurn churn)
    {
        using SceneSimulation simulation = new(StageWorkload.Compose(document, churn), run: StageWorkload.Defaults);

        StepSample[] samples = StepMeasurement.Measure(
            simulation,
            StageWorkload.StepSeconds,
            WarmupSteps,
            MeasuredSteps);

        return (samples, simulation.Scene.Entities.Length);
    }

    private void Report(string label, (StepSample[] Samples, int Entities) measured, long maxPerRun)
    {
        (StepSample[] samples, int entities) = measured;

        long allocated = 0;
        long peakAllocated = 0;
        long total = 0;
        long visible = 0;
        foreach (StepSample sample in samples)
        {
            allocated += sample.AllocatedBytes;
            peakAllocated = Math.Max(peakAllocated, sample.AllocatedBytes);
            total += sample.Render.Submitted;
            visible += sample.Render.Visible;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: {entities} entities, {allocated / (double)samples.Length:0.0} bytes/step "
            + $"(peak {peakAllocated}, run {allocated} of {maxPerRun}), "
            + $"commands {total / (double)samples.Length:0.0} total / {visible / (double)samples.Length:0.0} visible / "
            + $"{(total - visible) / (double)samples.Length:0.0} culled"));

        Assert.True(
            allocated <= maxPerRun,
            FormattableString.Invariant(
                $"{label} allocated {allocated} bytes over {samples.Length} steps, budget {maxPerRun}."));
    }
}

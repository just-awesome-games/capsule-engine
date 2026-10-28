using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using Capsule.Runtime;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Runtime;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class StageAllocationTests(ITestOutputHelper output)
{
    private const int WarmupSteps = 240;
    private const int SparksSpawned = StepMeasurement.MeasuredSteps / StageWorkload.StepsBetweenSpawns;

    // Over twice what a spark's entity, component list and renderer cost, and well under what one
    // stray per-step allocation in the engine would add.
    private const long SpawnBytesEach = 512;

    [Fact]
    public void AStageThatSpawnsNothing_AllocatesNothing()
    {
        SceneDocument document = StageWorkload.Build();

        AssertWithinBudget("no structural change", document, StageChurn.None, budget: 0);

        // The draw list is derived and rebuilt whole whenever anything joins or leaves. Rebuilding
        // it every step must still cost no allocation at all.
        AssertWithinBudget("draw list every step", document, StageChurn.DrawListOnly, budget: 0);
    }

    [Fact]
    public void AStageSpawningTwentyEntitiesASecond_AllocatesOnlyWhatItSpawns()
    {
        AssertWithinBudget(
            "20 spawns and despawns a second",
            StageWorkload.Build(),
            StageChurn.Spawning,
            budget: SparksSpawned * SpawnBytesEach);
    }

    [Fact]
    public void AStageSpawningFromAPool_AllocatesNothing()
    {
        AssertWithinBudget("20 spawns and despawns a second, from a pool", StageWorkload.Build(), StageChurn.Pooled, budget: 0);
    }

    // A wave spawned in one step is queued and drained at the end of it. The queue and its
    // membership index are the only things the drain itself keeps. The step allocates what the
    // entities cost and little more.
    [Fact]
    public void AWaveOfDeferredAdds_AllocatesNoMoreThanTheEntitiesItSpawns()
    {
        // Warmed first. The measurement is not taken against a cold JIT.
        Drain(256);

        const int Wave = 65536;
        long bytes = Drain(Wave);

        StepMeasurement.Report(output, $"{Wave} deferred adds", bytes, steps: 1, budget: Wave * SpawnBytesEach);

        // The only test in the collection that leaves a heap behind it. A background collection of
        // these entities inside a neighbour's measured window reads as a per-step allocation there.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static long Drain(int count)
    {
        Scene scene = new();
        using SceneSimulation simulation = new(scene);

        // From a step, not from scene entry. Only a mutation requested mid-step is deferred, and the
        // deferred queue is the thing being measured.
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
        // Beside the executable, where SceneComposer looks. SceneComposerTests writes here too.
        // StageAllocationCollection disables parallelization. The delete below cannot race it.
        string directory = Path.Combine(AppContext.BaseDirectory, "assets");
        string path = Path.Combine(directory, StageWorkload.DocumentName + ShippedSceneDocument.Extension);
        Directory.CreateDirectory(directory);

        try
        {
            ShippedSceneDocument.Write(StageWorkload.Build(), path, CompressionLevel.Fastest);

            SceneComposer composer = new(StageWorkload.Scenes(), new HostPlatform());
            using SceneHost host = new(
                SceneTransition.ToName(StageWorkload.DocumentName, null),
                composer.Resolve,
                StageWorkload.Defaults);

            Scene opened = host.Scene;
            File.Delete(path);

            for (int index = 0; index < 15; index++)
            {
                host.Run.RequestRestart();
                host.Step(SceneFixtures.Step(index));
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

    // Runs the stage with churn, writes what it drew, and asserts its steps fit the budget and its frames allocate nothing.
    private void AssertWithinBudget(string label, SceneDocument document, StageChurn churn, long budget)
    {
        using SceneSimulation simulation = new(StageWorkload.Compose(document, churn), run: StageWorkload.Defaults);
        StepSample[] samples = StepMeasurement.Measure(simulation, WarmupSteps);
        int entities = simulation.Scene.Entities.Length;

        long allocated = 0;
        long peakAllocated = 0;
        long built = 0;
        long total = 0;
        long visible = 0;
        foreach (StepSample sample in samples)
        {
            allocated += sample.StepBytes;
            peakAllocated = Math.Max(peakAllocated, sample.StepBytes);
            built += sample.ViewBytes;
            total += sample.Render.Submitted;
            visible += sample.Render.Visible;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: {entities} entities, peak {peakAllocated} bytes in a step, "
            + $"commands {total / (double)samples.Length:0.0} total / {visible / (double)samples.Length:0.0} visible / "
            + $"{(total - visible) / (double)samples.Length:0.0} culled"));

        StepMeasurement.Report(output, label, allocated, samples.Length, budget);
        StepMeasurement.Report(output, $"{label}, building frames", built, samples.Length);
    }
}

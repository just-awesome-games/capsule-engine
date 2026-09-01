using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;
using Xunit.Abstractions;

namespace Capsule.Tests.Performance;

[Collection(StagePerformanceCollection.Name)]
public sealed class StagePerformanceTests(ITestOutputHelper output)
{
    private const int WarmupSteps = 240;
    private const int MeasuredSteps = 600;
    private const int SparksSpawned = MeasuredSteps / StageWorkload.StepsBetweenSpawns;

    // One spark is an entity, its component list and a renderer; 512 bytes is well over twice
    // what those cost, and well under what one stray per-step allocation in the engine would add.
    private const long SpawnBytesEach = 512;

    // The step measures in the tens of microseconds against a 16.7 ms budget, so this sits some
    // fifty times above it: a collapse trips it and a drift does not, which is all a wall-clock
    // number on a shared runner can honestly claim.
    private static readonly TimeSpan MaxMeanStep = TimeSpan.FromMilliseconds(1);

    // A wave of 65536 deferred adds measures in the tens of milliseconds drained linearly and in
    // thousands drained by the square; this sits an order of magnitude clear of both.
    private static readonly TimeSpan MaxWaveDrain = TimeSpan.FromMilliseconds(400);

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

    // A wave spawned in one step is queued and drained at the end of it. Both halves of that used
    // to cost the square of the wave: the queue was scanned for duplicates on every add, and then
    // drained by taking the front off it. Linear, this batch is tens of milliseconds; quadratic it
    // was four and a half seconds, so a budget this far above the one still catches the other.
    [Fact]
    public void AWaveOfDeferredAdds_CostsNoMoreThanTheEntitiesItSpawns()
    {
        // Warmed first, so the measurement is not taken against a cold JIT.
        Drain(256);

        const int Wave = 65536;
        (TimeSpan elapsed, long bytes) = Drain(Wave);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Wave} deferred adds: {elapsed.TotalMilliseconds:0.000} ms, {bytes} bytes ({(double)bytes / Wave:0} each)"));

        Assert.True(
            elapsed < MaxWaveDrain,
            FormattableString.Invariant($"{Wave} deferred adds took {elapsed.TotalMilliseconds:0.000} ms, which is not linear in the queue."));

        // The queue and its membership index are the only things the drain itself keeps, so the
        // step costs what the entities cost and little more.
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

    private static (TimeSpan Elapsed, long Bytes) Drain(int count)
    {
        Scene scene = new();
        using SceneSimulation simulation = new(scene);

        // From Update, not from scene entry: only a mutation requested mid-step is deferred, and
        // the deferred queue is the thing being measured.
        scene.Add(new Spawner(count));

        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        simulation.Step(SceneFixtures.Step(0));

        return (Stopwatch.GetElapsedTime(start), GC.GetAllocatedBytesForCurrentThread() - bytes);
    }

    private sealed class Spawner(int count) : Entity(Vector2.Zero)
    {
        private bool _spawned;

        public override void Update(in StepContext context)
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
        string directory = Path.Combine(AppContext.BaseDirectory, "assets", "scenes");
        string path = Path.Combine(directory, StageWorkload.DocumentName + ".scene.json");
        Directory.CreateDirectory(directory);

        try
        {
            SceneDocumentFile.Save(StageWorkload.Build(), path);
            output.WriteLine(FormattableString.Invariant($"scene document: {new FileInfo(path).Length} bytes"));

            SceneComposer composer = new(StageWorkload.Scenes());

            long coldBytes = GC.GetAllocatedBytesForCurrentThread();
            long coldStart = Stopwatch.GetTimestamp();
            using SceneHost host = new(
                SceneTarget.ForName(StageWorkload.DocumentName),
                composer.Resolve,
                StageWorkload.Defaults);
            ReportTransition("boot", coldStart, coldBytes);

            Scene opened = host.Scene;
            File.Delete(path);

            const int Restarts = 15;
            double[] elapsed = new double[Restarts];
            long restartBytes = 0;
            for (int index = 0; index < Restarts; index++)
            {
                host.Scene.RequestRestart();

                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                host.Step(Scenes.SceneFixtures.Step(index));
                elapsed[index] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                restartBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }

            double first = elapsed[0];
            Array.Sort(elapsed);
            output.WriteLine(FormattableString.Invariant(
                $"restart: first {first:0.000} ms, median {elapsed[Restarts / 2]:0.000} ms, {restartBytes} bytes"));

            Assert.IsType<StageWorkload.StageScene>(host.Scene);
            Assert.NotSame(opened, host.Scene);
            Assert.Equal(StageWorkload.PlacedEntities + 1, host.Scene.Entities.Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (StepSample[] Samples, int Entities) Measure(SceneDocument document, StageChurn churn)
    {
        using SceneSimulation simulation = new(StageWorkload.Compose(document, churn), null, StageWorkload.Defaults);

        StepSample[] samples = StepMeasurement.Measure(
            simulation,
            StageWorkload.StepSeconds,
            WarmupSteps,
            MeasuredSteps);

        return (samples, simulation.Scene.Entities.Length);
    }

    private void ReportTransition(string label, long startTimestamp, long startBytes)
    {
        double elapsed = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

        output.WriteLine(FormattableString.Invariant(
            $"{label}: {elapsed:0.000} ms, {GC.GetAllocatedBytesForCurrentThread() - startBytes} bytes"));
    }

    private void Report(string label, (StepSample[] Samples, int Entities) measured, long maxPerRun)
    {
        (StepSample[] samples, int entities) = measured;

        long allocated = 0;
        long peakAllocated = 0;
        TimeSpan measuredDuration = TimeSpan.Zero;
        TimeSpan peakDuration = TimeSpan.Zero;
        long total = 0;
        long visible = 0;
        foreach (StepSample sample in samples)
        {
            allocated += sample.AllocatedBytes;
            peakAllocated = Math.Max(peakAllocated, sample.AllocatedBytes);
            measuredDuration += sample.Duration;
            peakDuration = sample.Duration > peakDuration ? sample.Duration : peakDuration;
            total += sample.Render.TotalQuads;
            visible += sample.Render.VisibleQuads;
        }

        TimeSpan mean = measuredDuration / samples.Length;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: {entities} entities, {allocated / (double)samples.Length:0.0} bytes/step "
            + $"(peak {peakAllocated}, run {allocated} of {maxPerRun}), "
            + $"mean {mean.TotalMilliseconds * 1000.0:0.00} us, peak {peakDuration.TotalMilliseconds * 1000.0:0.00} us, "
            + $"quads {total / (double)samples.Length:0.0} total / {visible / (double)samples.Length:0.0} visible / "
            + $"{(total - visible) / (double)samples.Length:0.0} culled"));

        Assert.True(
            allocated <= maxPerRun,
            FormattableString.Invariant(
                $"{label} allocated {allocated} bytes over {samples.Length} steps, budget {maxPerRun}."));
        Assert.True(
            mean < MaxMeanStep,
            FormattableString.Invariant($"{label} averaged {mean.TotalMilliseconds:0.000} ms a step."));
    }
}

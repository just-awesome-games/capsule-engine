using System.Diagnostics;
using System.Globalization;
using Capsule.Input;
using Capsule.Maps;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Verify;
using Xunit.Abstractions;

namespace Capsule.Tests.Performance;

/// <summary>
/// What the engine costs to run one screen of a real game, and the ceilings that cost is held
/// under. What is asserted is allocation and file access: those two are deterministic, where a
/// duration threshold on a shared runner measures the runner. Every measurement, durations
/// included, is written to the test output for a person reading the run.
/// </summary>
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

    [Fact]
    public void AStageThatSpawnsNothing_AllocatesNothing()
    {
        Map map = StageWorkload.Build();

        Report("no structural change", Measure(map, StageChurn.None, maxPerRun: 0));

        // The draw list is derived and rebuilt whole whenever anything joins or leaves. Rebuilding
        // it every step must still cost no allocation at all.
        Report("draw list every step", Measure(map, StageChurn.DrawListOnly, maxPerRun: 0));
    }

    [Fact]
    public void AStageSpawningTwentyEntitiesASecond_AllocatesOnlyWhatItSpawns()
    {
        Report(
            "20 spawns and despawns a second",
            Measure(StageWorkload.Build(), StageChurn.Spawning, maxPerRun: SparksSpawned * SpawnBytesEach));
    }

    // The most frequent transition a game performs is dying and resuming at a checkpoint, and it
    // happens between two fixed steps with a frame waiting on it. Deleting the map first is the
    // assertion: a restart that read the file could not survive it.
    [Fact]
    public void ARestart_ComposesTheStageAgainWithoutReadingItsMapFromDisk()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Maps");
        string path = Path.Combine(directory, StageWorkload.MapName + ".map.json");
        Directory.CreateDirectory(directory);

        try
        {
            MapFile.Save(StageWorkload.Build(), path);
            output.WriteLine(FormattableString.Invariant($"map file: {new FileInfo(path).Length} bytes"));

            SceneComposer composer = new(StageWorkload.Scenes());

            long coldBytes = GC.GetAllocatedBytesForCurrentThread();
            long coldStart = Stopwatch.GetTimestamp();
            using SceneHost host = new(
                SceneTarget.ForMap(StageWorkload.MapName),
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

    private static (VerifyRunResult Result, VerifyFrameMetrics[] Metrics, int Entities) Measure(
        Map map,
        StageChurn churn,
        long maxPerRun)
    {
        DeviceSnapshot[] script = new DeviceSnapshot[WarmupSteps + MeasuredSteps];
        Array.Fill(script, DeviceSnapshot.Empty);
        VerifyFrameMetrics[] metrics = new VerifyFrameMetrics[MeasuredSteps];

        using SceneSimulation simulation = new(StageWorkload.Compose(map, churn), null, StageWorkload.Defaults);

        VerifyRunResult result = VerifyRunner.Run(
            simulation,
            new ActionBindings(),
            script,
            new VerifyRunOptions(StageWorkload.StepSeconds, WarmupSteps, MaxAllocatedBytesPerRun: maxPerRun),
            metrics);

        return (result, metrics, simulation.Scene.Entities.Length);
    }

    private void ReportTransition(string label, long startTimestamp, long startBytes)
    {
        double elapsed = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

        output.WriteLine(FormattableString.Invariant(
            $"{label}: {elapsed:0.000} ms, {GC.GetAllocatedBytesForCurrentThread() - startBytes} bytes"));
    }

    private void Report(string label, (VerifyRunResult Result, VerifyFrameMetrics[] Metrics, int Entities) measured)
    {
        (VerifyRunResult result, VerifyFrameMetrics[] metrics, int entities) = measured;

        long total = 0;
        long visible = 0;
        TimeSpan peak = TimeSpan.Zero;
        foreach (VerifyFrameMetrics frame in metrics)
        {
            total += frame.Render.TotalQuads;
            visible += frame.Render.VisibleQuads;
            peak = frame.Duration > peak ? frame.Duration : peak;
        }

        TimeSpan mean = result.MeasuredDuration / result.MeasuredSteps;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: {entities} entities, {result.AllocatedBytes / (double)result.MeasuredSteps:0.0} bytes/step "
            + $"(peak {result.PeakFrameAllocatedBytes}, run {result.AllocatedBytes} of {result.MaxAllocatedBytesPerRun}), "
            + $"mean {mean.TotalMilliseconds * 1000.0:0.00} us, peak {peak.TotalMilliseconds * 1000.0:0.00} us, "
            + $"quads {total / (double)metrics.Length:0.0} total / {visible / (double)metrics.Length:0.0} visible / "
            + $"{(total - visible) / (double)metrics.Length:0.0} culled"));

        Assert.Equal(MeasuredSteps, result.MeasuredSteps);
        Assert.True(
            result.AllocationBudgetSatisfied,
            FormattableString.Invariant(
                $"{label} allocated {result.AllocatedBytes} bytes over {result.MeasuredSteps} steps, budget {result.MaxAllocatedBytesPerRun}."));
        Assert.True(
            mean < MaxMeanStep,
            FormattableString.Invariant($"{label} averaged {mean.TotalMilliseconds:0.000} ms a step."));
    }
}

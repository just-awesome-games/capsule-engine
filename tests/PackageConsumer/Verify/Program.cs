using System.Numerics;
using Capsule.Input;
using Capsule.Maps;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Generated;
using Capsule.Verify;
using PackageConsumer.Game;

namespace PackageConsumer.Verify;

/// <summary>
/// The consumer's headless verify run: read the map the build hook shipped, compose the
/// map-backed scene the game declares, drive it through a scripted input sequence, assert what it
/// ended up in and what it allocated, and write the artifacts. Published NativeAOT and executed
/// in CI, this is what proves a Capsule game boots rather than merely publishing.
/// </summary>
internal static class Program
{
    private const double StepSeconds = 1.0 / 60.0;
    private const int WarmupSteps = 30;
    private const int AdvanceSteps = 30;

    // Where a shipped game's maps are, and what the boot verbs resolve a map name against.
    private const string MapPath = "Assets/Maps/room.map.json";

    public static int Main(string[] args)
    {
        string outputDirectory = Path.GetFullPath(
            args is [string requested, ..] ? requested : Path.Combine("artifacts", "verify"));

        try
        {
            return Run(outputDirectory);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run(string outputDirectory)
    {
        ActionBindings bindings = new();
        ConsumerInput.Bind(bindings);

        DeviceSnapshot[] script = Script();
        VerifyFrameMetrics[] metrics = new VerifyFrameMetrics[script.Length - WarmupSteps];

        using SceneSimulation simulation = new(
            new Room(new MapSceneContext(RoomMap(), GameEntities.Registry)),
            null,
            new SceneDefaults(new Vector2(320f, 180f), TextureSampling.Point));

        float startX = simulation.Scene.FindSingle<Marker>().Position.X;

        VerifyRunResult result = VerifyRunner.Run(
            simulation,
            bindings,
            script,
            new VerifyRunOptions(
                StepSeconds,
                WarmupSteps: WarmupSteps,
                MaxAllocatedBytesPerStep: 0,
                MaxAllocatedBytesPerRun: 0),
            metrics);

        VerifyRunner.CaptureArtifacts(simulation, result, new VerifyArtifactSink(outputDirectory));

        float finalX = simulation.Scene.FindSingle<Marker>().Position.X;
        bool stateSatisfied =
            result.CompletedSteps == script.Length &&
            result.MeasuredSteps == script.Length - WarmupSteps &&
            result.ExitRequested &&
            finalX == startX + AdvanceSteps;

        if (!stateSatisfied)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"Package consumer verify: state assertions failed; {result.CompletedSteps}/{script.Length} steps, exit {result.ExitRequested}, marker {startX} -> {finalX}."));
            return 1;
        }

        if (!result.AllocationBudgetSatisfied)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"Package consumer verify: allocation budget failed; total {result.AllocatedBytes} bytes, peak frame {result.PeakFrameAllocatedBytes} bytes."));
            return 1;
        }

        Console.WriteLine(
            FormattableString.Invariant(
                $"Package consumer verify passed: {result.CompletedSteps} steps, {result.AllocatedBytes} measured bytes, artifacts in {outputDirectory}."));
        return 0;
    }

    private static DeviceSnapshot[] Script()
    {
        DeviceSnapshot[] snapshots = new DeviceSnapshot[WarmupSteps + AdvanceSteps + 1];
        Array.Fill(snapshots, DeviceSnapshot.Empty.With(Key.D), WarmupSteps, AdvanceSteps);
        snapshots[^1] = DeviceSnapshot.Of(Key.Escape);

        return snapshots;
    }

    // Read from beside the executable, exactly as the engine's own boot verbs read it: the file
    // the build hook derived from the game's Tiled source, through the source-generated reader,
    // in a binary published ahead of time. That whole chain is what this run exists to exercise.
    private static Map RoomMap() =>
        MapFile.Load(Path.Combine(AppContext.BaseDirectory, MapPath));
}

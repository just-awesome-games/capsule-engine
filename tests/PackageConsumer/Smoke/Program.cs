using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Input;
using Capsule.Maps;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Generated;
using PackageConsumer.Game;

namespace PackageConsumer.Smoke;

/// <summary>
/// The consumer's headless boot check: read the map the build hook shipped, compose the
/// map-backed scene the game declares, drive it through a scripted input sequence, and assert
/// where it ended up. Published ahead of time and executed in CI, this is what proves a Capsule
/// game boots rather than merely publishing.
/// </summary>
internal static class Program
{
    private const double StepSeconds = 1.0 / 60.0;
    private const int AdvanceSteps = 30;

    // Where a shipped game's maps are, and what the boot verbs resolve a map name against.
    private const string MapPath = "Assets/Maps/room.map.json";

    // The other half of the shipped plane: a map authored by hand in Capsule's own format rather
    // than in Tiled, and the two asset domains this consumer authors.
    private const string NativeMapPath = "Assets/Maps/hall.map.json";

    public static int Main()
    {
        try
        {
            return Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run()
    {
        ActionBindings bindings = new();
        ConsumerInput.Bind(bindings);
        InputState input = new(bindings);

        using SceneSimulation simulation = new(
            new Room(new MapSceneContext(RoomMap(), GameEntities.Registry)),
            null,
            new SceneDefaults(new Vector2(320f, 180f), TextureSampling.Point));

        float startX = simulation.Scene.FindSingle<Marker>().Position.X;

        DeviceSnapshot[] script = Script();
        int steps = 0;
        while (steps < script.Length)
        {
            input.Advance(script[steps]);
            simulation.Step(new StepContext(StepSeconds, input, steps));
            steps++;

            if (simulation.ExitRequested)
            {
                break;
            }
        }

        float finalX = simulation.Scene.FindSingle<Marker>().Position.X;
        RenderMetrics render = simulation.View.Metrics;
        bool booted =
            steps == script.Length &&
            simulation.ExitRequested &&
            finalX == startX + AdvanceSteps &&
            render.VisibleQuads > 0 &&
            ContentShipped();

        if (!booted)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"Package consumer smoke failed: {steps}/{script.Length} steps, exit {simulation.ExitRequested}, marker {startX} -> {finalX}, {render.VisibleQuads}/{render.TotalQuads} quads, content {ContentShipped()}."));
            return 1;
        }

        Console.WriteLine(
            FormattableString.Invariant(
                $"Package consumer smoke passed: {steps} steps, marker {startX} -> {finalX}, {render.VisibleQuads}/{render.TotalQuads} quads."));
        return 0;
    }

    private static DeviceSnapshot[] Script()
    {
        DeviceSnapshot[] snapshots = new DeviceSnapshot[AdvanceSteps + 1];
        Array.Fill(snapshots, DeviceSnapshot.Empty.With(Key.D), 0, AdvanceSteps);
        snapshots[^1] = DeviceSnapshot.Of(Key.Escape);

        return snapshots;
    }

    // Read from beside the executable, exactly as the engine's own boot verbs read it: the file
    // the build hook derived from the game's Tiled source, through the source-generated reader,
    // in a binary published ahead of time. That whole chain is what this run exists to exercise.
    private static Map RoomMap() =>
        MapFile.Load(Path.Combine(AppContext.BaseDirectory, MapPath));

    // The rest of the shipped plane, asserted rather than merely published: a hand-authored map
    // arrives validated and stamped as derived, and every asset is beside the executable under
    // the domain it was authored in, named by the handle the generator built from that file.
    private static bool ContentShipped()
    {
        Map hall = MapFile.Load(Path.Combine(AppContext.BaseDirectory, NativeMapPath));

        return hall.Source is { Tool: "native" }
            && GameAssets.Textures.Marker.Name == "marker"
            && GameAssets.Audio.StepSoft.Name == "step-soft"
            && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets/textures/marker.png"))
            && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets/audio/step-soft.wav"));
    }
}

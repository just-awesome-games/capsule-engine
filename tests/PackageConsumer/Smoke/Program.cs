using System.Numerics;
using Capsule;
using Capsule.Assets;
using Capsule.Assets.Generated;
using Capsule.Input;
using Capsule.Maps;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Generated;
using PackageConsumer.Game;

namespace PackageConsumer.Smoke;

internal static class Program
{
    private const double StepSeconds = 1.0 / 60.0;
    private const int AdvanceSteps = 30;

    private const string MapPath = "assets/maps/room.map.json";

    private const string NativeMapPath = "assets/maps/hall.map.json";

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

    private static Map RoomMap() =>
        MapFile.Load(Path.Combine(AppContext.BaseDirectory, MapPath));

    private static bool ContentShipped()
    {
        Map hall = MapFile.Load(Path.Combine(AppContext.BaseDirectory, NativeMapPath));
        TextureHandle marker = GameAssets.Textures.Marker;
        AudioHandle step = GameAssets.Audio.StepSoft;

        return hall.Source is { Tool: "native" }
            && marker.Name == "marker"
            && step.Name == "step-soft"
            && Shipped("textures", marker.Name, marker.Extension)
            && Shipped("audio", step.Name, step.Extension);
    }

    private static bool Shipped(string domain, string name, string extension) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", domain, name + extension));
}

using System.Numerics;
using Capsule.Assets;
using Capsule.Assets.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Generated;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;

namespace Capsule.AotSmoke;

internal static class Program
{
    private const double StepSeconds = 1.0 / 60.0;

    private const int IdleSteps = 60;

    private const string RoomPath = "assets/scenes/room.scene.json";

    private const string NativeScenePath = "assets/scenes/hall.scene.json";

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
        GameInput.Bind(bindings);
        InputState input = new(bindings);

        using SceneSimulation simulation = new(
            new Room(new SceneContent(Document(RoomPath), GameEntities.Registry)),
            null,
            new SceneDefaults(TextureSampling.Point));

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

        RenderMetrics render = simulation.View.Metrics;
        bool contentShipped = ContentShipped();
        bool booted =
            steps == script.Length &&
            simulation.ExitRequested &&
            render.Visible > 0 &&
            contentShipped;

        if (!booted)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed: {steps}/{script.Length} steps, exit {simulation.ExitRequested}, {render.Visible}/{render.Submitted} commands, content {contentShipped}."));
            return 1;
        }

        Console.WriteLine(
            FormattableString.Invariant(
                $"AOT smoke passed: {steps} steps, {render.Visible}/{render.Submitted} commands, content shipped."));
        return 0;
    }

    private static DeviceSnapshot[] Script()
    {
        DeviceSnapshot[] snapshots = new DeviceSnapshot[IdleSteps + 1];
        Array.Fill(snapshots, DeviceSnapshot.Empty, 0, IdleSteps);
        snapshots[^1] = DeviceSnapshot.Of(Key.Escape);

        return snapshots;
    }

    private static SceneDocument Document(string path) =>
        SceneDocumentFile.Load(Path.Combine(AppContext.BaseDirectory, path));

    // Every texture the shell would make resident at boot, so a handle the build registered with
    // no file behind it fails here rather than in front of a window.
    private static bool ContentShipped()
    {
        SceneDocument hall = Document(NativeScenePath);
        AudioHandle step = GameAssets.Audio.StepSoft;

        return hall.Source is { Tool: "native" }
            && Shipped(GameAssets.Textures.Player)
            && Shipped(GameAssets.Textures.Tiles)
            && Shipped(GameAssets.Textures.Sensor)
            && Shipped("audio", step.Name, step.Extension);
    }

    private static bool Shipped(TextureHandle texture) => Shipped("textures", texture.Name, texture.Extension);

    private static bool Shipped(string domain, string name, string extension) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", domain, name + extension));
}

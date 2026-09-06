using Capsule.Assets;
using Capsule.Assets.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Generated;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;

namespace Capsule.AotSmoke;

internal static class Program
{
    private const int IdleSteps = 60;

    private const string NativeScenePath = "assets/scenes/halls/hall.scene.json";

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
        InputTape tape = new InputScript()
            .Wait(IdleSteps)
            .Tap(Key.Escape)
            .Build();

        HeadlessRunResult result = CapsuleEngine.Configure("Capsule AOT Smoke", GameScenes.Registry)
            .WithBindings(GameInput.Bind)
            .WithSampling(TextureSampling.Point)
            .WithoutCrashLog()
            .WithoutLogging()
            .RunHeadless<Room>(tape);

        bool contentShipped = ContentShipped();
        bool booted =
            result.Steps == tape.Count &&
            result.ExitRequested &&
            result.Metrics.Visible > 0 &&
            contentShipped;

        if (!booted)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed: {result.Steps}/{tape.Count} steps, exit {result.ExitRequested}, {result.Metrics.Visible}/{result.Metrics.Submitted} commands, content {contentShipped}."));
            return 1;
        }

        Console.WriteLine(
            FormattableString.Invariant(
                $"AOT smoke passed: {result.Steps} steps, {result.Metrics.Visible}/{result.Metrics.Submitted} commands, content shipped."));
        return 0;
    }

    private static SceneDocument Document(string path) =>
        SceneDocumentFile.Load(Path.Combine(AppContext.BaseDirectory, path));

    // Every texture the sample's scenes make resident, so a handle the build registered with no
    // file behind it fails here rather than in front of a window.
    private static bool ContentShipped()
    {
        SceneDocument hall = Document(NativeScenePath);
        AudioHandle step = GameAssets.Audio.StepSoft;

        return hall.Source is { Tool: "native" }
            && Shipped(GameAssets.Textures.Actors.Player)
            && Shipped(GameAssets.Textures.Tiles)
            && Shipped(GameAssets.Textures.Sensor)
            && Shipped("audio", step.Name, step.Extension);
    }

    private static bool Shipped(TextureHandle texture) => Shipped("textures", texture.Name, texture.Extension);

    private static bool Shipped(string domain, string name, string extension) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", domain, name + extension));
}

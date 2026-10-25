using System.Globalization;
using System.Reflection;
using Capsule.Bench.Logic;
using Capsule.Bench.Logic.Scenes;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;

namespace Capsule.Bench;

// `suite` and `assets` are the bench's commands. Anything else is the engine's own command line plus
// `--soak-seconds <n>`.
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "suite")
        {
            return Suite.Run(args[1..]);
        }

        if (args.Length > 0 && args[0] == "assets")
        {
            return AssetBench.Run(args[1..]);
        }

        if (args.Length > 0 && args[0] == "latency")
        {
            return LatencyBench.Run(args[1..]);
        }

        // The latency run's child: the directory its measurement and injector write to, and the frame
        // cap it runs at without vertical sync, 0 for vertical sync.
        string? latency = TakeOption(ref args, LatencyBench.DirectoryOption);
        int cap = TakeOption(ref args, LatencyBench.CapOption) is { } hertz ? int.Parse(hertz, CultureInfo.InvariantCulture) : 0;

        int soak = Array.IndexOf(args, "--soak-seconds");
        if (soak >= 0)
        {
            if (soak + 1 >= args.Length || !double.TryParse(args[soak + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || !(seconds > 0))
            {
                Console.Error.WriteLine("--soak-seconds needs a positive number of seconds.");
                return 2;
            }

            Soak.Seconds = seconds;
            args = [.. args[..soak], .. args[(soak + 2)..]];
        }

        // A lane's row is its scene's class name or the variant it names. The engine runs the class, and
        // the scene reads the row from Workloads.Running as it composes. The surface is a boot option,
        // the Rendering lane's of the row. None, or a row this assembly does not know, boots the default.
        int named = Array.IndexOf(args, "--scene") + 1;
        string? row = named > 0 && named < args.Length ? args[named] : null;
        (Type Scene, WorkloadAttribute Lane)[] lanes =
        [
            .. Workloads.All
                .SelectMany(static scene => scene.GetCustomAttributes<WorkloadAttribute>().Select(lane => (scene, lane)))
                .Where(entry => (entry.lane.Row ?? entry.scene.Name) == row),
        ];
        if (lanes.Length > 0)
        {
            Workloads.Running = row;
            args = [.. args];
            args[named] = lanes[0].Scene.Name;
        }

        Surface surface = lanes.FirstOrDefault(static entry => entry.Lane.Kind == WorkloadKind.Rendering).Lane?.Surface ?? Surface.Canvas360;

        EngineBuilder engine;
        try
        {
            engine = CapsuleBoot.Configure("Capsule Bench", new DesktopPlatform())
                .WithCommandLine(args)
                .WithoutCrashLog()
                // A measured window paces the same whether or not it holds focus.
                .WithUnfocusedMaxFrameRate(0);
        }
        catch (CommandLineException failure)
        {
            return failure.Report();
        }

        engine = surface == Surface.Hd1080
            ? engine.WithWindow(1920, 1080).WithRenderResolution(1920, 1080).WithSampling(TextureSampling.Linear)
            : engine.WithWindow(1280, 720).WithRenderResolution(640, 360).WithSampling(TextureSampling.Point);

        if (cap > 0)
        {
            engine = engine.WithoutVSync().WithMaxFrameRate(cap);
        }

        if (latency is not null)
        {
            engine = engine.WithInputLatency(Path.Combine(latency, Injector.PressesFile));
            Injector.Start(latency, LatencyBench.WarmUpMs, LatencyBench.PressingMs);
        }

        return engine.RunScene<Still>();
    }

    // The value after option, removed from args with the option, or null when args holds none.
    private static string? TakeOption(ref string[] args, string option)
    {
        int at = Array.IndexOf(args, option);
        if (at < 0 || at + 1 >= args.Length)
        {
            return null;
        }

        string value = args[at + 1];
        args = [.. args[..at], .. args[(at + 2)..]];

        return value;
    }

    internal static string Describe(Surface surface) => surface == Surface.Hd1080 ? "1920x1080-linear" : "640x360-point";
}

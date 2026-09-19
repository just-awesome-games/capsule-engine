using System.Reflection;
using Capsule.Bench.Logic;
using Capsule.Bench.Logic.Scenes;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using Capsule.Runtime.Generated;

namespace Capsule.Bench;

// An ordinary Capsule game whose scenes are workloads: `suite` runs them all and records the
// results; anything else is the engine's own command line, so `--scene <Name>` runs one by hand.
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "suite")
        {
            return Suite.Run(args[1..]);
        }

        EngineBuilder engine;
        try
        {
            engine = CapsuleBoot.Configure("Capsule Bench", new DesktopPlatform())
                .WithCommandLine(args)
                .WithoutCrashLog();
        }
        catch (CommandLineException failure)
        {
            return failure.Report();
        }

        // The surface is a boot option, so it is the attribute's of whichever scene the command
        // line named; none, or one this assembly does not know, boots the default.
        Type? scene = Workloads.All.FirstOrDefault(scene => scene.Name == engine.SceneOverride);
        Surface surface = scene?.GetCustomAttribute<WorkloadAttribute>()?.Surface ?? Surface.Canvas360;

        engine = surface == Surface.Hd1080
            ? engine.WithWindow(1920, 1080).WithRenderResolution(1920, 1080).WithSampling(TextureSampling.Linear)
            : engine.WithWindow(1280, 720).WithRenderResolution(640, 360).WithSampling(TextureSampling.Point);

        return engine.RunScene<Still>();
    }

    internal static string Describe(Surface surface) => surface == Surface.Hd1080 ? "1920x1080-linear" : "640x360-point";
}

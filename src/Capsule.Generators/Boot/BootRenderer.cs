using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

// Writes CapsuleBoot.g.cs, a shell's entry point. Boot/CapsuleBoot.sample.g.cs shows the file.
internal static class BootRenderer
{
    private const string FileName = "CapsuleBoot.g.cs";

    // A statement's indent inside the entry point's methods.
    private const string Statement = "            ";

    internal static void Emit(SourceProductionContext context, BootPlan plan)
    {
        foreach (Diagnostic diagnostic in plan.Diagnostics.Items)
        {
            context.ReportDiagnostic(diagnostic);
        }

        if (plan.Generates)
        {
            context.AddSource(FileName, SourceText.From(Render(plan), Encoding.UTF8));
        }
    }

    internal static string Render(BootPlan plan)
    {
        string createScenes = RegistryProviderRenderer.SceneRegistryBody(plan.Providers.Items.Select(static provider => provider.QualifiedName), Statement);
        string addDrivers = string.Concat(plan.Providers.Items.Select(static provider => $"{Statement}{provider.QualifiedName}.AddDrivers(drivers);\n"))
            + string.Concat(plan.Drivers.Items.Select(static driver => $"{Statement}drivers.Add({InputDriverRenderer.Registration(driver)});\n"));

        return GeneratedFile.Write([], $$"""
                /// <summary>This game's entry point. Generated code. Do not edit.</summary>
                {{GeneratedFile.ExcludeFromCodeCoverage}}
                public static class CapsuleBoot
                {
                    private static global::Capsule.Scenes.SceneRegistry Scenes { get; } = CreateScenes();

                    private static global::Capsule.Input.InputDriverRegistry Drivers { get; } = CreateDrivers();

                    /// <summary>Begins the engine's configuration with every registry this game generates.</summary>
                    /// <param name="gameName">The game's display name. It titles the window, and its slug names the local folder that holds the saves and the crash log.</param>
                    /// <param name="platform">The platform module for the host family this shell targets.</param>
                    public static global::Capsule.Runtime.EngineBuilder Configure(string gameName, global::Capsule.Runtime.HostPlatform platform) =>
                        global::Capsule.Runtime.CapsuleEngine.Configure(gameName, platform, Scenes, Drivers);

                    private static global::Capsule.Scenes.SceneRegistry CreateScenes()
                    {
            {{createScenes}}        }

                    private static global::Capsule.Input.InputDriverRegistry CreateDrivers()
                    {
                        var drivers = new global::System.Collections.Generic.List<global::Capsule.Input.InputDriverRegistration>();
            {{addDrivers}}            return new global::Capsule.Input.InputDriverRegistry(drivers);
                    }
                }
            """);
    }
}

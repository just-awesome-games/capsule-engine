using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Writes CapsulePreloads.g.cs. Its module initializer records each class's reads with the engine before any of the
// assembly's code runs. Every collection of the class's objects sees them, a scene built with new included.
// Preloads/CapsulePreloads.sample.g.cs shows the file.
internal static class PreloadRenderer
{
    private const string FileName = "CapsulePreloads.g.cs";

    internal static void Emit(SourceProductionContext context, (PreloadPlan Plan, bool IsLogicAssembly) inputs)
    {
        if (inputs.IsLogicAssembly && !inputs.Plan.Classes.Items.IsEmpty)
        {
            GeneratedFile.Add(context, FileName, Render(inputs.Plan));
        }
    }

    internal static string Render(PreloadPlan plan) => GeneratedFile.Write([], $$"""
            /// <summary>The assets each scene, entity and component class in this assembly reads in its own code.</summary>
            /// <remarks>Generated code. Do not edit.</remarks>
            {{GeneratedFile.ExcludeFromCodeCoverage}}
            internal static class CapsulePreloads
            {
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Register()
                {
        {{string.Concat(plan.Classes.Items.Select(Entry))}}        }
            }
        """);

    private static string Entry(PreloadModel model) =>
        $"            global::Capsule.Assets.AssetCollection.RegisterClassAssets(typeof({model.TypeOfName}), static assets =>\n"
        + "            {\n"
        + string.Concat(model.Reads.Items.Select(static read => $"                assets.Add({read});\n"))
        + "            });\n";
}

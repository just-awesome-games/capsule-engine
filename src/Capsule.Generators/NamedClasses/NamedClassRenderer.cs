using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Writes a named registry's file, the registry a logic assembly hands the shell its classes through. A class the shell
// declares itself goes straight into CapsuleBoot. Drivers/CapsuleInputDrivers.sample.g.cs and
// Payloads/CapsuleScenePayloads.sample.g.cs show the two files.
internal static class NamedClassRenderer
{
    internal static void Emit(SourceProductionContext context, (NamedClassPlan Plan, bool IsLogicAssembly) inputs, NamedRegistry registry)
    {
        if (!inputs.IsLogicAssembly)
        {
            return;
        }

        GeneratedFile.Report(context, inputs.Plan.Diagnostics);

        GeneratedFile.Add(context, registry.ClassName + ".g.cs", Render(inputs.Plan, registry));
    }

    internal static string Render(NamedClassPlan plan, NamedRegistry registry) => GeneratedFile.Write([], $$"""
            /// <summary>Every {{registry.Noun}} this assembly declares.</summary>
            /// <remarks>Generated code. Do not edit.</remarks>
            {{GeneratedFile.ExcludeFromCodeCoverage}}
            internal static class {{registry.ClassName}}
            {
        {{GeneratedFile.Registrations(registry.Registration, plan.Registered.Items.Select(model => Registration(model, registry)))}}
            }
        """);

    // One registration's construction, written the same way in a logic assembly's registry and in the
    // shell's entry point.
    internal static string Registration(NamedClassModel model, NamedRegistry registry) =>
        $"new {registry.Registration}({CodeText.Literal(model.TypeName)}, static () => new {model.QualifiedName}())";
}

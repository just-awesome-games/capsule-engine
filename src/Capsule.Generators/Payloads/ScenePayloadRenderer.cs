using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Writes CapsuleScenePayloads.g.cs, the registry a logic assembly hands the shell its payloads through.
// A payload the shell declares itself goes straight into CapsuleBoot. Payloads/CapsuleScenePayloads.sample.g.cs
// shows the file.
internal static class ScenePayloadRenderer
{
    private const string FileName = "CapsuleScenePayloads.g.cs";

    internal static void Emit(SourceProductionContext context, (ScenePayloadPlan Payloads, bool IsLogicAssembly) inputs)
    {
        if (!inputs.IsLogicAssembly)
        {
            return;
        }

        GeneratedFile.Report(context, inputs.Payloads.Diagnostics);

        GeneratedFile.Add(context, FileName, Render(inputs.Payloads));
    }

    internal static string Render(ScenePayloadPlan plan) => GeneratedFile.Write([], $$"""
            /// <summary>Every scene payload this assembly declares.</summary>
            /// <remarks>Generated code. Do not edit.</remarks>
            {{GeneratedFile.ExcludeFromCodeCoverage}}
            internal static class CapsuleScenePayloads
            {
        {{GeneratedFile.Registrations("global::Capsule.Scenes.ScenePayloadRegistration", plan.Registered.Items.Select(Registration))}}
            }
        """);

    // One registration's construction, written the same way in a logic assembly's registry and in the
    // shell's entry point.
    internal static string Registration(ScenePayloadModel model) =>
        $"new global::Capsule.Scenes.ScenePayloadRegistration({CodeText.Literal(model.TypeName)}, static () => new {model.QualifiedName}())";
}

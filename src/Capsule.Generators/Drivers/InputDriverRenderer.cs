using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

// Writes CapsuleInputDrivers.g.cs, the registry a logic assembly hands the shell its drivers through.
// A driver the shell declares itself goes straight into CapsuleBoot. Drivers/CapsuleInputDrivers.sample.g.cs
// shows the file.
internal static class InputDriverRenderer
{
    private const string FileName = "CapsuleInputDrivers.g.cs";

    internal static void Emit(SourceProductionContext context, InputDriverInputs inputs)
    {
        if (!inputs.IsLogicAssembly)
        {
            return;
        }

        foreach (Diagnostic diagnostic in inputs.Drivers.Diagnostics.Items)
        {
            context.ReportDiagnostic(diagnostic);
        }

        context.AddSource(FileName, SourceText.From(Render(inputs.Drivers), Encoding.UTF8));
    }

    internal static string Render(InputDriverPlan plan) => GeneratedFile.Write([], $$"""
            /// <summary>Every input driver this assembly declares. Generated code. Do not edit.</summary>
            {{GeneratedFile.ExcludeFromCodeCoverage}}
            internal static class CapsuleInputDrivers
            {
        {{GeneratedFile.Registrations("global::Capsule.Input.InputDriverRegistration", plan.Registered.Items.Select(Registration))}}
            }
        """);

    // One registration's construction, written the same way in a logic assembly's registry and in the
    // shell's entry point.
    internal static string Registration(InputDriverModel model) =>
        $"new global::Capsule.Input.InputDriverRegistration({CodeText.Literal(model.TypeName)}, static () => new {model.QualifiedName}())";
}
